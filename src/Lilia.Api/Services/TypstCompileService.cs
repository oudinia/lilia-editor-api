using System.Diagnostics;
using System.Text;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Services;

/// <summary>
/// Invokes the Typst CLI to compile a Typst source string to PDF or
/// SVG. Strategic role: the FAST half of the hybrid compile pipeline
/// — Typst handles live-preview compiles in &lt;500ms (vs pdflatex's
/// 8-30s); pdflatex stays on the publication-export path.
///
/// Uses a temp-dir-per-compile pattern: each compile writes its source
/// + asset bundle to its own scratch directory, runs typst-cli with
/// that as cwd, then cleans up. Resilient to concurrent compiles
/// (different temp dirs) and to CLI failures (process exit code +
/// captured stderr).
///
/// Production deployment: typst CLI must be present in the API
/// container. Dockerfile snippet documented in the FT-TYPST-001 spec.
/// In dev: install via `~/.local/bin/typst` (see binary releases at
/// https://github.com/typst/typst/releases).
///
/// Telemetry: every compile failure writes a `partial_parse` event
/// with `source_format='typst'` so we see real-world Typst quirks
/// (catalog gaps, font missing, etc.) as users hit them.
/// </summary>
public class TypstCompileService : ITypstCompileService
{
    private readonly ILogger<TypstCompileService> _logger;
    private readonly IImportTelemetrySink _telemetry;
    private readonly TypstCompileOptions _options;

    public TypstCompileService(
        ILogger<TypstCompileService>? logger = null,
        IImportTelemetrySink? telemetry = null,
        TypstCompileOptions? options = null)
    {
        _logger = logger ?? NullLogger<TypstCompileService>.Instance;
        _telemetry = telemetry ?? new NoopImportTelemetrySink();
        _options = options ?? new TypstCompileOptions();
    }

    /// <summary>
    /// Compile a Typst source string to the requested output format.
    /// Returns success + bytes on the happy path; failure + error
    /// message + stderr on the unhappy path. Never throws — every
    /// failure mode flows through TypstCompileResult.
    /// </summary>
    public async Task<TypstCompileResult> CompileAsync(
        string source,
        TypstOutputFormat format = TypstOutputFormat.Svg,
        CancellationToken ct = default)
    {
        var binary = ResolveBinary();
        if (binary is null)
        {
            return TypstCompileResult.Failure(
                "Typst binary not found. Install via the binary release " +
                "(https://github.com/typst/typst/releases) or set the " +
                $"{TypstBinaryEnvVar} environment variable to its full path.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"typst-compile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            var sourceFile = Path.Combine(workDir, "main.typ");
            var outputExt = format switch
            {
                TypstOutputFormat.Pdf => "pdf",
                TypstOutputFormat.Svg => "svg",
                TypstOutputFormat.Png => "png",
                _ => "svg",
            };
            var outputFile = Path.Combine(workDir, $"main.{outputExt}");

            await File.WriteAllTextAsync(sourceFile, source, Encoding.UTF8, ct);

            var stopwatch = Stopwatch.StartNew();
            var psi = new ProcessStartInfo
            {
                FileName = binary,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("compile");
            psi.ArgumentList.Add("--root");
            psi.ArgumentList.Add(workDir);
            psi.ArgumentList.Add(sourceFile);
            psi.ArgumentList.Add(outputFile);

            using var proc = new Process { StartInfo = psi };
            var stderrSb = new StringBuilder();
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) stderrSb.AppendLine(e.Data);
            };

            proc.Start();
            proc.BeginErrorReadLine();

            // Apply per-compile timeout to keep one bad doc from
            // wedging the worker.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.PerCompileTimeout);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                _telemetry.Record(new ImportTelemetryRecord
                {
                    EventKind = "partial_parse",
                    Severity = "warn",
                    SourceFormat = "typst",
                    TokenOrEnv = "compile_timeout",
                    SampleText = source.Length > 200 ? source[..200] : source,
                });
                return TypstCompileResult.Failure(
                    $"Typst compile timed out after {_options.PerCompileTimeout.TotalSeconds}s");
            }

            stopwatch.Stop();
            var stderr = stderrSb.ToString();

            if (proc.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Typst compile failed (exit={ExitCode}, ms={Ms}): {Stderr}",
                    proc.ExitCode, stopwatch.ElapsedMilliseconds, stderr);
                _telemetry.Record(new ImportTelemetryRecord
                {
                    EventKind = "partial_parse",
                    Severity = "warn",
                    SourceFormat = "typst",
                    TokenOrEnv = "compile_error",
                    SampleText = stderr.Length > 200 ? stderr[..200] : stderr,
                });
                return TypstCompileResult.Failure(stderr.Length > 0
                    ? stderr
                    : $"Typst compile exited with code {proc.ExitCode}");
            }

            if (!File.Exists(outputFile))
            {
                return TypstCompileResult.Failure(
                    $"Typst reported success but {outputExt} output not found");
            }

            var bytes = await File.ReadAllBytesAsync(outputFile, ct);
            return TypstCompileResult.Ok(bytes, format, stopwatch.Elapsed);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    /// <summary>
    /// Resolve the typst binary path. Order:
    ///   1. <c>TYPST_BINARY</c> env var (explicit override)
    ///   2. <c>~/.local/bin/typst</c> (dev install location)
    ///   3. <c>/usr/local/bin/typst</c> (production container default)
    ///   4. <c>typst</c> on PATH
    ///
    /// Returns null if nothing resolves so the caller can degrade
    /// gracefully (PreviewService falls back to pdflatex).
    /// </summary>
    private static string? ResolveBinary()
    {
        var env = Environment.GetEnvironmentVariable(TypstBinaryEnvVar);
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            var local = Path.Combine(home, ".local", "bin", "typst");
            if (File.Exists(local)) return local;
        }

        if (File.Exists("/usr/local/bin/typst")) return "/usr/local/bin/typst";
        if (File.Exists("/usr/bin/typst")) return "/usr/bin/typst";

        // PATH lookup as last resort.
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir, "typst");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private const string TypstBinaryEnvVar = "TYPST_BINARY";
}

public interface ITypstCompileService
{
    Task<TypstCompileResult> CompileAsync(
        string source,
        TypstOutputFormat format = TypstOutputFormat.Svg,
        CancellationToken ct = default);
}

public enum TypstOutputFormat
{
    Svg,
    Pdf,
    Png,
}

public class TypstCompileOptions
{
    /// <summary>Hard timeout per compile. Keeps a runaway document from
    /// wedging the worker thread. Defaults to 30s — plenty for typical
    /// docs which compile in &lt;500ms.</summary>
    public TimeSpan PerCompileTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class TypstCompileResult
{
    public bool Success { get; private init; }
    public byte[]? Output { get; private init; }
    public TypstOutputFormat Format { get; private init; }
    public TimeSpan Elapsed { get; private init; }
    public string? Error { get; private init; }

    public static TypstCompileResult Ok(byte[] output, TypstOutputFormat format, TimeSpan elapsed) =>
        new() { Success = true, Output = output, Format = format, Elapsed = elapsed };

    public static TypstCompileResult Failure(string error) =>
        new() { Success = false, Error = error };
}
