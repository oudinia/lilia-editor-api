using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Lilia.Api.E2E.Tests.LatexCoverage;

/// <summary>
/// Local pdflatex runs for the compile layer. pdflatex is looked up on PATH;
/// when absent the layer is recorded as <c>skip</c>, never as a pass.
/// </summary>
public static class LatexToolchain
{
    // A valid 1×1 PNG. Every \includegraphics target in the exported .tex gets
    // one, so the compile layer tests the LaTeX the exporter wrote rather than
    // whether an image was uploaded (the catalog's documents reference image
    // files that are never part of the upload).
    private static readonly byte[] PlaceholderPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static string? PdfLatex { get; } = Find("pdflatex");

    public sealed record CompileResult(bool Ok, string Detail);

    /// <summary>
    /// One <c>pdflatex -interaction=nonstopmode -halt-on-error</c> pass. A single
    /// pass is enough to decide pass/fail: a second pass only resolves
    /// references, and unresolved references are warnings, never errors.
    /// </summary>
    public static async Task<CompileResult> CompileLatexAsync(string tex)
    {
        var dir = Directory.CreateTempSubdirectory("latexcov-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "main.tex"), tex);
            foreach (Match m in Regex.Matches(tex, @"\\includegraphics\s*(?:\[[^\]]*\])?\s*\{([^}]+)\}"))
                WritePlaceholder(dir.FullName, m.Groups[1].Value.Trim());

            var (code, output) = await RunAsync(PdfLatex!, dir.FullName, TimeSpan.FromSeconds(90),
                "-interaction=nonstopmode", "-halt-on-error", "-no-shell-escape", "main.tex");
            var pdf = Path.Combine(dir.FullName, "main.pdf");
            if (code == 0 && File.Exists(pdf)) return new CompileResult(true, "");

            var log = Path.Combine(dir.FullName, "main.log");
            var text = File.Exists(log) ? File.ReadAllText(log) : output;
            return new CompileResult(false, "pdflatex: " + FirstLatexError(text));
        }
        finally
        {
            TryDelete(dir.FullName);
        }
    }

    private static void WritePlaceholder(string root, string target)
    {
        if (target.Contains("..") || Path.IsPathRooted(target)) return;
        // \includegraphics may omit the extension; \graphicspath{{./figures/}} is what
        // the exporter writes, so cover both the literal path and figures/<path>.
        var names = Path.HasExtension(target) ? new[] { target } : new[] { target + ".png" };
        foreach (var name in names)
            foreach (var path in new[] { Path.Combine(root, name), Path.Combine(root, "figures", name) })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) File.WriteAllBytes(path, PlaceholderPng);
            }
    }

    private static string FirstLatexError(string log)
    {
        var lines = log.Split('\n');
        var i = Array.FindIndex(lines, l => l.StartsWith('!'));
        if (i < 0) return "no PDF produced";
        // "! Undefined control sequence." is useless without the l.NN line after it.
        var context = lines.Skip(i + 1).Take(4).FirstOrDefault(l => l.StartsWith("l.", StringComparison.Ordinal));
        return Short(lines[i].TrimStart('!', ' ') + (context != null ? " — " + context.Trim() : ""));
    }

    private static string Short(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 220 ? s[..220] + "…" : s;
    }

    private static async Task<(int Code, string Output)> RunAsync(string exe, string cwd, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return (-1, $"timed out after {timeout.TotalSeconds}s");
        }
        return (p.ExitCode, await stdout + await stderr);
    }

    private static string? Find(string name)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var d in dirs)
        {
            var candidate = Path.Combine(d, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* temp dir */ }
    }
}
