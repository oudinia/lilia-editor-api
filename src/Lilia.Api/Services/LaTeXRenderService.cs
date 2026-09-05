using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lilia.Core.Blocks;
using Lilia.Engines;

namespace Lilia.Api.Services;

public interface ILaTeXRenderService
{
    Task<byte[]> RenderToPdfAsync(string latex, int timeout = 30);
    Task<byte[]> RenderToPdfAsync(string latex, string engine, int timeout = 30);
    Task<byte[]> RenderToPdfTolerantAsync(string latex, int timeout = 60);

    /// <summary>
    /// Compile and return the PDF alongside the block → page map read from the
    /// .aux. One compile, two outputs — the map is a by-product of a render that
    /// had to happen anyway.
    /// </summary>
    Task<(byte[] Pdf, IReadOnlyDictionary<Guid, int> PageMap)> RenderToPdfWithPageMapAsync(
        string latex, string engine = "pdflatex", int timeout = 60);

    Task<byte[]> RenderToPngAsync(string latex, int dpi = 150, int timeout = 30);
    Task<byte[]> RenderBlockToPngAsync(string latexFragment, string? preamble = null, int dpi = 150);
    Task<string> RenderToSvgAsync(string latexFragment, bool displayMode = true);
    Task<LatexValidationResult> ValidateAsync(string latex);
    Task<LatexValidationResult> ValidateAsync(string latex, string engine);

    /// <summary>
    /// Compile a multi-file LaTeX project (pdflatex pass 1 → BibTeX) just far
    /// enough to produce the bibliography, and return the resulting
    /// <c>{mainStem}.bbl</c> content (or null if BibTeX produced none). Used by
    /// the arXiv-ready export to bundle a precompiled .bbl so the submission
    /// compiles with pdflatex alone. <paramref name="files"/> are (relative
    /// path, text content): main.tex, references.bib, preamble/chapters, etc.
    /// </summary>
    Task<string?> GenerateBblAsync(
        IReadOnlyList<(string Path, string Content)> files,
        string mainStem = "main", int timeoutSeconds = 60);
}

/// <summary>
/// Full result of a LaTeX validation run — includes the parsed error for persistence/telemetry.
/// </summary>
public record LatexValidationResult(
    bool Valid,
    string? Error,
    string[] Warnings,
    LaTeXErrorParser.ParsedLatexError? ParsedError,
    int DurationMs,
    // Compile metadata for the full-page validation view (§7). Captured during
    // the validate compile and previously dropped before the response.
    string Engine = "",
    string Log = ""
);

public class LaTeXRenderService : ILaTeXRenderService
{
    private readonly ILogger<LaTeXRenderService> _logger;
    /// <summary>
    /// Process-wide bound on concurrent LaTeX compiles.
    ///
    /// <para><b>This is NOT replaced by queue concurrency, despite what P2.4
    /// stage 2 originally proposed.</b> The plan reads "replace
    /// <c>LaTeXRenderService._semaphore</c> with queue concurrency: the semaphore
    /// bounds one process, a prefetch count bounds the cluster." That only holds
    /// if <i>every</i> compile goes through the queue — and stage 3 deliberately
    /// keeps per-block validation synchronous, because the author is waiting and
    /// the problem there is latency, not reliability.</para>
    ///
    /// <para>So the paths this guards are not all queueable. Removing it would
    /// leave validation — the hottest path in the system, called on every block
    /// blur — with no bound at all, while the queue bounded only exports. The
    /// two are complementary, not alternatives: the queue bounds how much work
    /// is <i>admitted</i>, this bounds how many <c>pdflatex</c> processes exist
    /// at once, which is the actual scarce resource.</para>
    ///
    /// <para>Queue concurrency is therefore set BELOW this number, so the two
    /// cannot add up to more compiles than the box was sized for.</para>
    /// </summary>
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
    private static readonly ConcurrentDictionary<string, (bool Valid, string? Error, string[] Warnings, string Engine, string Log, int DurationMs, DateTime CachedAt)> _validationCache = new();
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
        return await RenderToPdfAsync(latex, "pdflatex", timeout);
    }

    /// <summary>
    /// Engine-aware render. engine: "pdflatex" | "xelatex" | "lualatex".
    /// Falls back to pdflatex for any unrecognised value. Callers reading
    /// Document.LatexEngine pass it straight through.
    /// </summary>
    public async Task<byte[]> RenderToPdfAsync(string latex, string engine, int timeout = 30)
    {
        await _semaphore.WaitAsync();
        try
        {
            var (pdf, _, _) = await CompileLatexAsync(latex, timeout, engine: ResolveEngine(engine));
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
            var (pdf, _, _) = await CompileLatexAsync(latex, timeout, tolerant: true);
            return pdf;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Compile and return both the PDF and the block → page map read from the
    /// .aux.
    ///
    /// <para>One compile, two outputs, on purpose. The map is a by-product of a
    /// render that had to happen anyway — asking for it separately would mean
    /// compiling the document twice to learn something the first compile
    /// already wrote down.</para>
    ///
    /// <para>An empty map is a normal result, not a failure: a document whose
    /// blocks carry no labels (anything rendered before this shipped) simply has
    /// nothing to report.</para>
    /// </summary>
    public async Task<(byte[] Pdf, IReadOnlyDictionary<Guid, int> PageMap)> RenderToPdfWithPageMapAsync(
        string latex, string engine = "pdflatex", int timeout = 60)
    {
        await _semaphore.WaitAsync();
        try
        {
            var (pdf, _, aux) = await CompileLatexAsync(
                latex, timeout, tolerant: true, engine: ResolveEngine(engine));
            return (pdf, AuxPageMap.Parse(aux));
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
            var (pdf, _, _) = await CompileLatexAsync(latex, timeout);
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

    /// <summary>
    /// One line describing content too tall for the page, or null.
    ///
    /// LaTeX has TWO ways of saying this and which one appears depends on
    /// whether the content floats:
    ///
    /// <list type="bullet">
    /// <item><c>Overfull \vbox (525.0pt too high)</c> — un-floated content, e.g.
    /// a bare <c>tabular</c> in the text flow.</item>
    /// <item><c>LaTeX Warning: Float too large for page by 1161.16pt</c> — the
    /// case Lilia actually produces, because table blocks are emitted inside a
    /// <c>table</c> float.</item>
    /// </list>
    ///
    /// Neither is cosmetic: a <c>tabular</c> cannot break across pages, so past
    /// the text height the remainder falls off the bottom — and the compile
    /// still exits 0. Summarised rather than passed through, because a document
    /// can emit one per page and the largest overflow is the actionable number.
    /// </summary>
    private static string? SummarisePageOverflow(string[] allWarnings)
    {
        var tooTall = allWarnings
            .Where(w => w.Contains("Overfull \\vbox") || w.Contains("Float too large for page"))
            .ToArray();
        if (tooTall.Length == 0) return null;

        // "(525.0pt too high)" for vbox, "by 1161.16606pt" for floats.
        var worst = tooTall
            .Select(w => System.Text.RegularExpressions.Regex.Match(
                w, @"(?:\(|by )([\d.]+)pt"))
            .Where(m => m.Success)
            .Select(m => double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pt) ? pt : 0)
            .DefaultIfEmpty(0)
            .Max();

        var where = tooTall.Length == 1 ? "in one place" : $"in {tooTall.Length} places";
        var byHowMuch = worst > 0 ? $", by up to {worst:0}pt" : "";

        return $"Content is too tall for the page {where}{byHowMuch}. Anything past the bottom "
             + "margin is cut off in the PDF — a table this tall has to be able to break "
             + "across pages.";
    }

    public async Task<LatexValidationResult> ValidateAsync(string latex) =>
        await ValidateAsync(latex, "pdflatex");

    /// <summary>
    /// Engine-aware validate. engine: "pdflatex" | "xelatex" | "lualatex".
    /// Falls back to pdflatex for any unrecognised value. Cache key
    /// includes the engine so a fontspec block doesn't get a stale
    /// pdflatex "Undefined control sequence" cached against it.
    /// </summary>
    public async Task<LatexValidationResult> ValidateAsync(string latex, string engine)
    {
        var resolvedEngine = ResolveEngine(engine);
        // Cache key includes engine so swapping engines invalidates cleanly.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(latex + "|" + resolvedEngine)));
        if (_validationCache.TryGetValue(hash, out var cached))
        {
            _logger.LogDebug("Validation cache hit for hash {Hash} ({Engine})", hash[..12], resolvedEngine);
            return new LatexValidationResult(cached.Valid, cached.Error, cached.Warnings, null, cached.DurationMs, cached.Engine, cached.Log);
        }

        await _semaphore.WaitAsync();
        try
        {
            // Double-check after acquiring semaphore (another thread may have cached it)
            if (_validationCache.TryGetValue(hash, out cached))
                return new LatexValidationResult(cached.Valid, cached.Error, cached.Warnings, null, cached.DurationMs, cached.Engine, cached.Log);

            var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-latex-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);

            try
            {
                var texPath = Path.Combine(tmpDir, "document.tex");
                var logPath = Path.Combine(tmpDir, "document.log");
                await File.WriteAllTextAsync(texPath, latex);

                // BuildPdflatexArgs works for lualatex/xelatex too — same
                // arg shape (`-interaction`, `-output-directory`, halt mode).
                // Only the precompiled .fmt is pdflatex-specific; skip it
                // when the engine differs so we don't hand pdflatex's fmt
                // file to lualatex.
                // The precompiled .fmt bakes in \documentclass{standalone}, so it
                // only works for FRAGMENTS. A self-contained document (its own
                // \documentclass — e.g. contextual per-block validation) would
                // collide → "Two \documentclass commands". Use the format only
                // for pdflatex AND only when the input has no \documentclass.
                var usePrecompiled = resolvedEngine == "pdflatex"
                    && !latex.Contains(@"\documentclass", StringComparison.Ordinal);
                var args = BuildPdflatexArgs(texPath, tmpDir, usePrecompiled: usePrecompiled);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (exitCode, _, stderr) = await RunProcessAsync(resolvedEngine, args, tmpDir, 15);
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
                        durationMs,
                        resolvedEngine,
                        logContent
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
                        // The \hbox variants above are genuinely cosmetic — a
                        // line a few points too wide. The \vbox ones are not
                        // symmetrical with them: "Overfull \vbox" means content
                        // is too TALL for the page, i.e. material running off
                        // the bottom, and it had been discarded here because the
                        // name looks like its cosmetic siblings.
                        .Where(w => !w.Contains("Underfull \\vbox"))
                        .Where(w => !w.Contains("Font shape"))
                        .Where(w => !w.Contains("Size substitutions"))
                        .Where(w => !w.Contains("microtype"))
                        // Benign single-pass rerun notices — our validation
                        // compiles once, so hyperref/cross-ref/.out "rerun"
                        // warnings always appear and are NOT actionable. Without
                        // this, every block that compiles cleanly would still
                        // surface a "warning" (and the per-block "✓ compiles"
                        // indicator would never read green).
                        .Where(w => !w.Contains("rerunfilecheck"))
                        .Where(w => !w.Contains("Rerun to get"))
                        .Where(w => !w.Contains("Rerun LaTeX"))
                        .Where(w => !w.Contains("Label(s) may have changed"))
                        // Self-inflicted: UnicodeShimService injects
                        // \newunicodechar for every mapped codepoint present,
                        // and the class or another package may already define
                        // some. The author neither caused it nor can act on it,
                        // and a document with a few Greek letters emits ten of
                        // these — enough on its own to exhaust the cap below
                        // and hide the warnings that matter.
                        .Where(w => !w.Contains("newunicodechar"))
                        // The two page-overflow signals are excluded here and
                        // hoisted ahead of the cap instead — see below. Note
                        // "\\v": in C# a single backslash-v is a vertical tab,
                        // which compiles happily and matches nothing.
                        .Where(w => !w.Contains("Overfull \\vbox"))
                        .Where(w => !w.Contains("Float too large for page"))
                        .Take(10)
                        .ToArray();

                    // Two warnings that mean content is MISSING from the PDF are
                    // hoisted ahead of the capped list above. Both would
                    // otherwise be crowded out by ordinary noise — which is
                    // exactly what was happening — and both describe text the
                    // reader will not see, so they outrank anything cosmetic.

                    // Content ran off the bottom of a page. "Overfull \vbox" is
                    // how a tabular too tall to fit reports itself; it had been
                    // filtered alongside the \hbox variants, which really are
                    // cosmetic.
                    var overflow = SummarisePageOverflow(allWarnings);

                    // Silently dropped glyphs. These never reach the filter
                    // above at all: a "Missing character:" line contains none of
                    // the words "Warning", "Underfull" or "Overfull". Scanned
                    // from the WHOLE log, and reported as DISTINCT code points —
                    // TeX emits one line per occurrence, so a paragraph of CJK
                    // produces hundreds.
                    var glyphs = LaTeXGlyphScanner.Describe(LaTeXGlyphScanner.Scan(logContent));

                    actionableWarnings =
                    [
                        .. new[] { glyphs, overflow }.Where(w => w is not null).Select(w => w!),
                        .. actionableWarnings,
                    ];

                    result = new LatexValidationResult(true, null, actionableWarnings, null, durationMs, resolvedEngine, logContent);
                }

                // Cache the result (with compile metadata so cache hits still
                // surface engine/log/timing for the validation page).
                CacheValidationResult(hash, (result.Valid, result.Error, result.Warnings, result.Engine, result.Log, result.DurationMs));

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

    private static void CacheValidationResult(string hash, (bool Valid, string? Error, string[] Warnings, string Engine, string Log, int DurationMs) result)
    {
        // Simple eviction: clear all when we exceed the limit.
        // This is acceptable because validation results are cheap to recompute
        // relative to the cost of a more complex eviction policy.
        if (_validationCache.Count >= MaxValidationCacheEntries)
        {
            _validationCache.Clear();
        }

        _validationCache.TryAdd(hash, (result.Valid, result.Error, result.Warnings, result.Engine, result.Log, result.DurationMs, DateTime.UtcNow));
    }

    /// <summary>Normalise the engine name to one of the three supported binaries.</summary>
    internal static string ResolveEngine(string? engine) => (engine ?? "").ToLowerInvariant() switch
    {
        "xelatex"  => "xelatex",
        "lualatex" => "lualatex",
        _          => "pdflatex",
    };

    public async Task<string?> GenerateBblAsync(
        IReadOnlyList<(string Path, string Content)> files,
        string mainStem = "main", int timeoutSeconds = 60)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-bbl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Write the project out, creating subdirs (chapters/, etc.).
            foreach (var (rel, content) in files)
            {
                var full = Path.Combine(tmpDir, rel);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(full, content);
            }

            // Pass 1: pdflatex emits {mainStem}.aux with \citation/\bibdata/\bibstyle.
            // nonstopmode (no -halt-on-error) so a missing \includegraphics asset
            // doesn't abort before the .aux is written — BibTeX only needs the .aux.
            await RunProcessAsync("pdflatex", $"-interaction=nonstopmode {mainStem}.tex", tmpDir, timeoutSeconds);

            // BibTeX: {mainStem}.aux + references.bib -> {mainStem}.bbl.
            var (bibExit, bibOut, _) = await RunProcessAsync("bibtex", mainStem, tmpDir, timeoutSeconds);

            var bblPath = Path.Combine(tmpDir, $"{mainStem}.bbl");
            if (File.Exists(bblPath))
            {
                var bbl = await File.ReadAllTextAsync(bblPath);
                if (!string.IsNullOrWhiteSpace(bbl)) return bbl;
            }
            _logger.LogWarning("[bbl] BibTeX produced no .bbl (exit {Exit}): {Out}", bibExit, bibOut.Length > 400 ? bibOut[..400] : bibOut);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[bbl] .bbl generation failed");
            return null;
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private async Task<(byte[] Pdf, string Log, string Aux)> CompileLatexAsync(string latex, int timeout, bool tolerant = false, string engine = "pdflatex")
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-latex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            var texPath = Path.Combine(tmpDir, "document.tex");
            var pdfPath = Path.Combine(tmpDir, "document.pdf");
            var logPath = Path.Combine(tmpDir, "document.log");
            await File.WriteAllTextAsync(texPath, latex);

            // Run the chosen engine twice (for references). pdflatex is the
            // default; xelatex / lualatex take the same arg shape for our
            // use-case so the build-args helper just passes the command
            // through. Precompiled format is pdflatex-only, so other engines
            // skip it implicitly in BuildPdflatexArgs.
            for (int pass = 0; pass < 2; pass++)
            {
                var args = BuildPdflatexArgs(texPath, tmpDir, tolerant: tolerant);
                var (exitCode, stdout, stderr) = await RunProcessAsync(
                    engine,
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

            // Read the .aux before the finally block deletes the directory. It
            // carries one \newlabel per block label, which is where the page map
            // comes from — see AuxPageMap. Accurate only because the loop above
            // runs the engine twice: pass one writes the labels, pass two settles
            // the page numbers.
            var auxPath = Path.Combine(tmpDir, "document.aux");
            var aux = File.Exists(auxPath) ? await File.ReadAllTextAsync(auxPath) : "";

            return (pdf, log, aux);
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
