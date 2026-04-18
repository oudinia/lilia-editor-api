using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Lilia.Api.Services;

public interface ILaTeXRenderService
{
    Task<byte[]> RenderToPdfAsync(string latex, int timeout = 30);
    Task<byte[]> RenderToPdfTolerantAsync(string latex, int timeout = 60);
    Task<byte[]> RenderToPngAsync(string latex, int dpi = 150, int timeout = 30);
    Task<byte[]> RenderBlockToPngAsync(string latexFragment, string? preamble = null, int dpi = 150);
    Task<string> RenderToSvgAsync(string latexFragment, bool displayMode = true);
    Task<LatexValidationResult> ValidateAsync(string latex);
}

/// <summary>
/// Full result of a LaTeX validation run — includes the parsed error for persistence/telemetry.
/// </summary>
public record LatexValidationResult(
    bool Valid,
    string? Error,
    string[] Warnings,
    LaTeXErrorParser.ParsedLatexError? ParsedError,
    int DurationMs
);

public class LaTeXRenderService : ILaTeXRenderService
{
    private readonly ILogger<LaTeXRenderService> _logger;
    private static readonly SemaphoreSlim _semaphore = new(3, 3); // Max 3 concurrent compilations

    private const string PrecompiledFormatPath = "/tmp/lilia-latex-preamble/lilia-preamble";
    private static bool? _formatFileExists;

    private const string MinimalPreamble = @"\documentclass[preview,border=2pt]{standalone}
\usepackage[utf8]{inputenc}
\usepackage[T1]{fontenc}
\usepackage{amsmath,amssymb,amsfonts}
\usepackage{mathtools}
\usepackage{bm}
\usepackage{graphicx}
\usepackage{xcolor}
\usepackage{booktabs}
\usepackage{listings}
\usepackage{hyperref}
";

    /// <summary>
    /// In-memory cache for validation results, keyed by SHA-256 hash of the LaTeX content.
    /// Avoids re-running pdflatex when the same content is validated again.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (bool Valid, string? Error, string[] Warnings, DateTime CachedAt)> _validationCache = new();
    private const int MaxValidationCacheEntries = 1000;

    public LaTeXRenderService(ILogger<LaTeXRenderService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the precompiled .fmt file exists (cached after first check).
    /// </summary>
    private static bool HasPrecompiledFormat()
    {
        _formatFileExists ??= File.Exists(PrecompiledFormatPath + ".fmt");
        return _formatFileExists.Value;
    }

    /// <summary>
    /// Returns the pdflatex arguments, using the precompiled format if available.
    /// </summary>
    private static string BuildPdflatexArgs(string texPath, string outputDir, bool usePrecompiled = false, bool tolerant = false)
    {
        // Only use precompiled format for fragment validation (standalone class)
        // Full documents already have \documentclass — using -fmt would cause "Two \documentclass" error
        var fmtArg = usePrecompiled && HasPrecompiledFormat()
            ? $"-fmt={PrecompiledFormatPath} "
            : "";
        // tolerant=true drops -halt-on-error so pdflatex skips past recoverable
        // body errors (unbalanced \text commands, stray chars, etc.) and still
        // produces a PDF. Used for document export where a partial PDF is
        // strictly better than none. Validation paths keep strict mode.
        var haltArg = tolerant ? "" : "-halt-on-error ";
        return $"-interaction=nonstopmode {haltArg}--no-shell-escape {fmtArg}-output-directory {outputDir} {texPath}";
    }

    public async Task<byte[]> RenderToPdfAsync(string latex, int timeout = 30)
    {
        await _semaphore.WaitAsync();
        try
        {
            var (pdf, _) = await CompileLatexAsync(latex, timeout);
            return pdf;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Document-export variant: runs pdflatex without -halt-on-error so minor
    /// body errors produce a partial PDF rather than aborting with zero output.
    /// </summary>
    public async Task<byte[]> RenderToPdfTolerantAsync(string latex, int timeout = 60)
    {
        await _semaphore.WaitAsync();
        try
        {
            var (pdf, _) = await CompileLatexAsync(latex, timeout, tolerant: true);
            return pdf;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<byte[]> RenderToPngAsync(string latex, int dpi = 150, int timeout = 30)
    {
        await _semaphore.WaitAsync();
        try
        {
            var (pdf, _) = await CompileLatexAsync(latex, timeout);
            return await PdfToPngAsync(pdf, dpi);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<byte[]> RenderBlockToPngAsync(string latexFragment, string? preamble = null, int dpi = 150)
    {
        var fullSource = MinimalPreamble;
        if (!string.IsNullOrEmpty(preamble))
            fullSource += preamble + "\n";
        fullSource += "\\begin{document}\n" + latexFragment + "\n\\end{document}\n";

        return await RenderToPngAsync(fullSource, dpi, 15);
    }

    public async Task<string> RenderToSvgAsync(string latexFragment, bool displayMode = true)
    {
        await _semaphore.WaitAsync();
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-svg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);

            try
            {
                // Build a standalone document for the formula
                var math = displayMode
                    ? $"\\[{latexFragment}\\]"
                    : $"${latexFragment}$";

                var fullSource = MinimalPreamble + "\\begin{document}\n" + math + "\n\\end{document}\n";

                var texPath = Path.Combine(tmpDir, "formula.tex");
                var dviPath = Path.Combine(tmpDir, "formula.dvi");
                var svgPath = Path.Combine(tmpDir, "formula.svg");
                await File.WriteAllTextAsync(texPath, fullSource);

                // Step 1: latex -> DVI (faster than pdflatex for single formulas)
                var (exitCode, _, stderr) = await RunProcessAsync(
                    "latex",
                    $"-interaction=nonstopmode -halt-on-error --no-shell-escape -output-directory {tmpDir} {texPath}",
                    tmpDir,
                    10
                );

                if (exitCode != 0)
                    throw new InvalidOperationException($"LaTeX compilation failed: {(stderr.Length > 300 ? stderr[..300] : stderr)}");

                if (!File.Exists(dviPath))
                    throw new InvalidOperationException("DVI was not generated");

                // Step 2: DVI -> SVG via dvisvgm
                var (svgExit, _, svgStderr) = await RunProcessAsync(
                    "dvisvgm",
                    $"--no-fonts --exact-bbox --zoom=1.4 -o {svgPath} {dviPath}",
                    tmpDir,
                    10
                );

                if (svgExit != 0 || !File.Exists(svgPath))
                    throw new InvalidOperationException($"SVG conversion failed: {(svgStderr.Length > 300 ? svgStderr[..300] : svgStderr)}");

                var svg = await File.ReadAllTextAsync(svgPath);

                // Clean up XML declaration if present
                if (svg.StartsWith("<?xml"))
                {
                    var idx = svg.IndexOf("?>");
                    if (idx > 0) svg = svg[(idx + 2)..].TrimStart();
                }

                return svg;
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<LatexValidationResult> ValidateAsync(string latex)
    {
        // Check cache first (no semaphore needed for read)
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(latex)));
        if (_validationCache.TryGetValue(hash, out var cached))
        {
            _logger.LogDebug("Validation cache hit for hash {Hash}", hash[..12]);
            return new LatexValidationResult(cached.Valid, cached.Error, cached.Warnings, null, 0);
        }

        await _semaphore.WaitAsync();
        try
        {
            // Double-check after acquiring semaphore (another thread may have cached it)
            if (_validationCache.TryGetValue(hash, out cached))
                return new LatexValidationResult(cached.Valid, cached.Error, cached.Warnings, null, 0);

            var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-latex-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);

            try
            {
                var texPath = Path.Combine(tmpDir, "document.tex");
                var logPath = Path.Combine(tmpDir, "document.log");
                await File.WriteAllTextAsync(texPath, latex);

                var args = BuildPdflatexArgs(texPath, tmpDir);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (exitCode, _, stderr) = await RunProcessAsync("pdflatex", args, tmpDir, 15);
                sw.Stop();
                var durationMs = (int)sw.ElapsedMilliseconds;

                var logContent = File.Exists(logPath)
                    ? await File.ReadAllTextAsync(logPath)
                    : "";

                LatexValidationResult result;

                if (exitCode != 0)
                {
                    var errorLines = logContent.Split('\n')
                        .Where(l => l.StartsWith("!") || l.Contains("Error"))
                        .Take(5)
                        .ToArray();

                    var errorMsg = errorLines.Length > 0
                        ? string.Join("\n", errorLines)
                        : stderr.Length > 500 ? stderr[..500] : stderr;

                    // Parse into structured error for telemetry
                    var rawForParsing = logContent.Length > 0 ? logContent : stderr;
                    var parsed = LaTeXErrorParser.Parse(rawForParsing);

                    result = new LatexValidationResult(
                        false,
                        $"LaTeX compilation failed:\n{errorMsg}",
                        [],
                        parsed,
                        durationMs
                    );
                }
                else
                {
                    // Classify warnings: filter out cosmetic noise, keep actionable ones
                    var allWarnings = logContent.Split('\n')
                        .Where(l => l.Contains("Warning") || l.Contains("Underfull") || l.Contains("Overfull"))
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToArray();

                    var actionableWarnings = allWarnings
                        .Where(w => !w.Contains("Overfull \\hbox"))
                        .Where(w => !w.Contains("Underfull \\hbox"))
                        .Where(w => !w.Contains("Overfull \\vbox"))
                        .Where(w => !w.Contains("Underfull \\vbox"))
                        .Where(w => !w.Contains("Font shape"))
                        .Where(w => !w.Contains("Size substitutions"))
                        .Where(w => !w.Contains("microtype"))
                        .Take(10)
                        .ToArray();

                    result = new LatexValidationResult(true, null, actionableWarnings, null, durationMs);
                }

                // Cache the result
                CacheValidationResult(hash, (result.Valid, result.Error, result.Warnings));

                return result;
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static void CacheValidationResult(string hash, (bool Valid, string? Error, string[] Warnings) result)
    {
        // Simple eviction: clear all when we exceed the limit.
        // This is acceptable because validation results are cheap to recompute
        // relative to the cost of a more complex eviction policy.
        if (_validationCache.Count >= MaxValidationCacheEntries)
        {
            _validationCache.Clear();
        }

        _validationCache.TryAdd(hash, (result.Valid, result.Error, result.Warnings, DateTime.UtcNow));
    }

    private async Task<(byte[] Pdf, string Log)> CompileLatexAsync(string latex, int timeout, bool tolerant = false)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-latex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            var texPath = Path.Combine(tmpDir, "document.tex");
            var pdfPath = Path.Combine(tmpDir, "document.pdf");
            var logPath = Path.Combine(tmpDir, "document.log");
            await File.WriteAllTextAsync(texPath, latex);

            // Run pdflatex twice (for references)
            for (int pass = 0; pass < 2; pass++)
            {
                var args = BuildPdflatexArgs(texPath, tmpDir, tolerant: tolerant);
                var (exitCode, stdout, stderr) = await RunProcessAsync(
                    "pdflatex",
                    args,
                    tmpDir,
                    timeout
                );

                // In tolerant mode, accept whatever PDF was produced even if
                // exit code indicates errors — the user gets a partial preview.
                if (exitCode != 0 && pass == 1 && !tolerant)
                {
                    var logContent = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";
                    var errorLines = logContent.Split('\n')
                        .Where(l => l.StartsWith("!") || l.Contains("Error"))
                        .Take(5);
                    throw new InvalidOperationException(
                        $"LaTeX compilation failed:\n{string.Join("\n", errorLines)}"
                    );
                }
            }

            if (!File.Exists(pdfPath))
            {
                // Tolerant mode should still surface "no PDF at all" — likely
                // preamble / package error, not a body glitch.
                var logContent = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";
                var errorLines = logContent.Split('\n')
                    .Where(l => l.StartsWith("!") || l.Contains("Error"))
                    .Take(5);
                throw new InvalidOperationException(
                    errorLines.Any()
                        ? $"LaTeX compilation failed:\n{string.Join("\n", errorLines)}"
                        : "PDF was not generated"
                );
            }

            var pdf = await File.ReadAllBytesAsync(pdfPath);
            var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";

            return (pdf, log);
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private async Task<byte[]> PdfToPngAsync(byte[] pdf, int dpi)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-png-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            var pdfPath = Path.Combine(tmpDir, "input.pdf");
            var outputPrefix = Path.Combine(tmpDir, "output");
            await File.WriteAllBytesAsync(pdfPath, pdf);

            await RunProcessAsync(
                "pdftoppm",
                $"-png -r {dpi} -singlefile {pdfPath} {outputPrefix}",
                tmpDir,
                10
            );

            var pngPath = outputPrefix + ".png";
            if (!File.Exists(pngPath))
                throw new InvalidOperationException("PNG conversion failed");

            return await File.ReadAllBytesAsync(pngPath);
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string command, string arguments, string workingDir, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException($"Process timed out after {timeoutSeconds}s");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}
