using System.Diagnostics;
using System.Text;

namespace Lilia.Api.Services;

public interface ILaTeXRenderService
{
    Task<byte[]> RenderToPdfAsync(string latex, int timeout = 30);
    Task<byte[]> RenderToPngAsync(string latex, int dpi = 150, int timeout = 30);
    Task<byte[]> RenderBlockToPngAsync(string latexFragment, string? preamble = null, int dpi = 150);
    Task<string> RenderToSvgAsync(string latexFragment, bool displayMode = true);
    Task<(bool Valid, string? Error, string[] Warnings)> ValidateAsync(string latex);
}

public class LaTeXRenderService : ILaTeXRenderService
{
    private readonly ILogger<LaTeXRenderService> _logger;
    private static readonly SemaphoreSlim _semaphore = new(3, 3); // Max 3 concurrent compilations

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

    public LaTeXRenderService(ILogger<LaTeXRenderService> logger)
    {
        _logger = logger;
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

                // Step 1: latex → DVI (faster than pdflatex for single formulas)
                var (exitCode, _, stderr) = await RunProcessAsync(
                    "latex",
                    $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                    tmpDir,
                    10
                );

                if (exitCode != 0)
                    throw new InvalidOperationException($"LaTeX compilation failed: {(stderr.Length > 300 ? stderr[..300] : stderr)}");

                if (!File.Exists(dviPath))
                    throw new InvalidOperationException("DVI was not generated");

                // Step 2: DVI → SVG via dvisvgm
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

    public async Task<(bool Valid, string? Error, string[] Warnings)> ValidateAsync(string latex)
    {
        await _semaphore.WaitAsync();
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-latex-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);

            try
            {
                var texPath = Path.Combine(tmpDir, "document.tex");
                var logPath = Path.Combine(tmpDir, "document.log");
                await File.WriteAllTextAsync(texPath, latex);

                var (exitCode, _, stderr) = await RunProcessAsync(
                    "pdflatex",
                    $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                    tmpDir,
                    15
                );

                var logContent = File.Exists(logPath)
                    ? await File.ReadAllTextAsync(logPath)
                    : "";

                if (exitCode != 0)
                {
                    var errorLines = logContent.Split('\n')
                        .Where(l => l.StartsWith("!") || l.Contains("Error"))
                        .Take(5)
                        .ToArray();

                    var errorMsg = errorLines.Length > 0
                        ? string.Join("\n", errorLines)
                        : stderr.Length > 500 ? stderr[..500] : stderr;

                    return (false, $"LaTeX compilation failed:\n{errorMsg}", []);
                }

                // Classify warnings: filter out cosmetic noise, keep actionable ones
                var allWarnings = logContent.Split('\n')
                    .Where(l => l.Contains("Warning") || l.Contains("Underfull") || l.Contains("Overfull"))
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                // Suppress cosmetic warnings that users can't act on
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

                return (true, null, actionableWarnings);
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

    private async Task<(byte[] Pdf, string Log)> CompileLatexAsync(string latex, int timeout)
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
                var (exitCode, stdout, stderr) = await RunProcessAsync(
                    "pdflatex",
                    $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                    tmpDir,
                    timeout
                );

                if (exitCode != 0 && pass == 1)
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
                throw new InvalidOperationException("PDF was not generated");

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
