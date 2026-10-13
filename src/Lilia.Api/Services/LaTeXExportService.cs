using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lilia.Core.Blocks;
using Lilia.Engines;
using Lilia.Engines.Themes;

namespace Lilia.Api.Services;

public class LaTeXExportService : ILaTeXExportService
{
    private readonly LiliaDbContext _context;
    private readonly IStorageService _storageService;
    private readonly ILogger<LaTeXExportService> _logger;
    private readonly Lilia.Import.Services.IImportTelemetrySink _telemetry;
    private readonly ILaTeXRenderService? _renderService;

    public LaTeXExportService(
        LiliaDbContext context,
        IStorageService storageService,
        ILogger<LaTeXExportService> logger,
        Lilia.Import.Services.IImportTelemetrySink? telemetry = null,
        ILaTeXRenderService? renderService = null)
    {
        _context = context;
        _storageService = storageService;
        _logger = logger;
        _telemetry = telemetry ?? new Lilia.Import.Services.NoopImportTelemetrySink();
        _renderService = renderService;
    }

    private const string ArxivReadmeNote =
        "\n\n--- arXiv submission ---\n" +
        "This project is arXiv-ready: a precompiled main.bbl is included, so it\n" +
        "compiles with pdflatex alone (no BibTeX run needed):\n\n" +
        "  pdflatex main.tex\n" +
        "  pdflatex main.tex\n\n" +
        "references.bib is kept for reference. Builds on a recent TeX Live; all\n" +
        "packages are standard CTAN. If you change the bibliography, re-run the\n" +
        "full pdflatex -> bibtex -> pdflatex -> pdflatex cycle to refresh main.bbl.\n";

    /// <summary>
    /// Test-only entry point that bypasses the DB. Generates the
    /// single-file LaTeX source from in-memory document + blocks. Used
    /// by LatexRoundtripTests to verify parse → blocks → export →
    /// re-parse preserves block structure across the corpus.
    /// </summary>
    public string BuildSingleFileLatex(
        Document doc,
        List<Block> blocks,
        List<BibliographyEntry> bibEntries,
        LaTeXExportOptions options)
    {
        bibEntries = WithTextEntries(blocks, bibEntries);
        var files = ApplyCitationBackend(GenerateSingleFile(doc, blocks, bibEntries, options), options);
        var main = files.First(f => f.Path == "main.tex").Content;
        var bib = files.FirstOrDefault(f => f.Path == "references.bib")?.Content;
        return bib is null ? main : WithBibInside(main, bib);
    }

    /// <summary>
    /// The one-file .tex carries its references.bib (MCP-89): it ended with \bibliography{references}
    /// and shipped no .bib, so every citation printed [?]. filecontents* writes the file on the first
    /// run, before \documentclass; BibTeX (or Overleaf, latexmk) then reads it as from the zip.
    /// </summary>
    internal static string WithBibInside(string main, string bib)
    {
        // A line that would end the environment early is the only thing that can't pass through.
        var safe = System.Text.RegularExpressions.Regex.Replace(bib, @"\\end\{filecontents\*?\}", "");
        var block = "% The bibliography, written to references.bib when this file is compiled.\n"
            + "\\begin{filecontents*}[overwrite]{references.bib}\n" + safe.TrimEnd() + "\n\\end{filecontents*}\n";
        var at = main.IndexOf("\\documentclass", StringComparison.Ordinal);
        if (at < 0) return block + main;
        var lineStart = main.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        return main[..lineStart] + block + main[lineStart..];
    }

    public async Task<Stream> ExportToZipAsync(Guid documentId, LaTeXExportOptions options)
    {
        var doc = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc == null)
            throw new ArgumentException("Document not found");

        var blocks = doc.Blocks.ToList();
        var bibEntries = doc.BibliographyEntries?.ToList() ?? new List<BibliographyEntry>();

        var projectFiles = GenerateProject(doc, blocks, bibEntries, options);

        // arXiv-ready: compile the project (pdflatex pass 1 → BibTeX) and bundle
        // the resulting main.bbl, so the submission compiles with pdflatex alone
        // (arXiv's safest path) without re-running BibTeX. Falls back gracefully
        // to references.bib-only if compilation is unavailable or fails.
        // arXiv .bbl is BibTeX-based — only meaningful for the natbib backend.
        // biblatex uses Biber (different .bbl + limited arXiv support), so skip.
        if (options.Arxiv && !IsBiblatex(options) && bibEntries.Count > 0 && _renderService != null)
        {
            var texFiles = projectFiles
                .Where(f => f.Path.EndsWith(".tex") || f.Path.EndsWith(".bib") || f.Path.EndsWith(".bst") || f.Path.EndsWith(".sty"))
                .Select(f => (f.Path, f.Content))
                .ToList();
            var bbl = await _renderService.GenerateBblAsync(texFiles);
            if (!string.IsNullOrWhiteSpace(bbl))
            {
                projectFiles.Add(new ProjectFile("main.bbl", bbl));
                _logger.LogInformation("[Export] arXiv-ready: bundled main.bbl ({Len} chars) for document {DocId}", bbl.Length, documentId);
            }
            else
            {
                _logger.LogWarning("[Export] arXiv-ready requested but .bbl generation failed for document {DocId}; shipping references.bib only", documentId);
            }

            // Append an arXiv-readiness note to the README.
            var ri = projectFiles.FindIndex(f => f.Path == "README.txt");
            if (ri >= 0)
                projectFiles[ri] = new ProjectFile("README.txt", projectFiles[ri].Content + ArxivReadmeNote);
        }

        // Build ZIP
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in projectFiles)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(file.Content);
                await entryStream.WriteAsync(bytes);
            }

            // Add images from storage
            if (options.IncludeImages)
            {
                await AddImagesToArchive(archive, blocks);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private List<ProjectFile> GenerateProject(
        Document doc,
        List<Block> blocks,
        List<BibliographyEntry> bibEntries,
        LaTeXExportOptions options)
    {
        bibEntries = WithTextEntries(blocks, bibEntries);
        var files = options.Structure switch
        {
            "multi" when options.MultiFileLayout == "overleaf" =>
                GenerateMultiFileOverleaf(doc, blocks, bibEntries, options),
            "multi" =>
                GenerateMultiFileFlat(doc, blocks, bibEntries, options),
            _ =>
                GenerateSingleFile(doc, blocks, bibEntries, options)
        };
        // A themed project carries its package (lilia-theme.sty, or a deck's beamertheme….sty), so
        // it compiles as downloaded (Overleaf included).
        var themeFiles = files.Where(f => f.Path.EndsWith(".tex", StringComparison.Ordinal))
            .SelectMany(f => ThemeCatalog.FilesUsedBy(f.Content))
            .DistinctBy(f => f.FileName)
            .ToList();
        foreach (var (name, source) in themeFiles)
            files.Add(new ProjectFile(name, source));
        return ApplyCitationBackend(files, options);
    }

    // The class the headings print for: in a class with \chapter a level-1 heading is a chapter
    // (HeadingCommands). Set once per export; the service is scoped, one document at a time.
    private string _headingClass = "article";

    // Whether a long table may be a longtable: not in a multi-column document, where longtable
    // is a hard error (RenderService.SupportsLongtable). Set with the heading class.
    private bool _longtableAllowed = true;

    // The check emitter, which renders every body block of the export too (BlocksToLatex), and
    // the document being exported, set with the heading class.
    private readonly RenderService _check = new(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<RenderService>.Instance);
    private Document? _doc;

    /// <summary>
    /// References written as text in a bibliography block (<c>@cite[key] Author - Title</c>), as
    /// <c>@misc</c> entries in the .bib beside the document's own. The check printed them; the
    /// export printed nothing and every citation of them was [?] (MCP-04, run 2's MCP-76).
    /// </summary>
    internal static List<BibliographyEntry> WithTextEntries(IEnumerable<Block> blocks, List<BibliographyEntry> bibEntries)
    {
        var known = bibEntries.Select(e => e.CiteKey).ToHashSet(StringComparer.Ordinal);
        var list = blocks.ToList();
        // BibTeX written in the block (MCP-81), then references written as text.
        var bibtex = BibliographyBlocks.BibTeX(list).Where(e => known.Add(e.CiteKey)).ToList();
        var extra = bibtex.Concat(BibliographyBlocks.Text(list)
            .Where(e => known.Add(e.Key))
            .Select(e => new BibliographyEntry
            {
                CiteKey = e.Key,
                EntryType = "misc",
                Data = JsonDocument.Parse(JsonSerializer.Serialize(new { note = e.Text })),
            }))
            .ToList();
        return extra.Count == 0 ? bibEntries : bibEntries.Concat(extra).ToList();
    }

    private void UseHeadingClass(Document doc, LaTeXExportOptions options)
    {
        _doc = doc;
        _headingClass = LaTeXPreambleBuilder.ResolveClassName(doc, options.DocumentClass);
        _longtableAllowed = RenderService.SupportsLongtable(doc);
    }

    // ── Single-file structure ──────────────────────────────────────────

    private List<ProjectFile> GenerateSingleFile(
        Document doc,
        List<Block> blocks,
        List<BibliographyEntry> bibEntries,
        LaTeXExportOptions options)
    {
        UseHeadingClass(doc, options);
        var files = new List<ProjectFile>();
        var sb = new StringBuilder();
        var usesNatbib = DocumentUsesNatbib(blocks);

        // The body's blocks, worked out before the preamble: the theme line's Index pins are
        // numbered by the headings the body will actually contain.
        // A book with front matter keeps its abstract in the body, among the front matter (MCP-91).
        var abstractInBody = RenderService.UsesBookDivisions(doc, blocks);
        var mainBlocks = BodyBlocks(doc.Title, blocks, abstractInBody);

        // Preamble embedded in main.tex
        sb.AppendLine(BuildDocumentClassDirective(doc, options));
        sb.AppendLine();
        sb.Append(GeneratePackageLines(doc, options, usesNatbib, mainBlocks));
        sb.AppendLine();

        // Document info
        sb.AppendLine("% Document info");
        AppendTitleMeta(sb, doc, blocks);
        sb.AppendLine();

        // Begin document
        sb.AppendLine(@"\begin{document}");
        sb.AppendLine();
        sb.AppendLine(@"\maketitle");
        sb.AppendLine();

        // Abstract (if exists)
        var abstractBlock = abstractInBody ? null : LeadAbstract(blocks);
        if (abstractBlock != null)
        {
            var text = GetContentText(abstractBlock);
            sb.AppendLine(@"\begin{abstract}");
            sb.AppendLine(FormatInlineContent(text));
            sb.AppendLine(@"\end{abstract}");
            sb.AppendLine();
        }

        // Main content — strip a duplicate-title leading heading. LaTeX
        // imports often promote \title{X} into a top-level heading
        // block whose text matches doc.Title; combined with \maketitle
        // above, the user sees the title twice.
        // Balanced-columns body wrapper (multicol). Empty for non-balanced
        // docs — the twocolumn class option handles those upstream.
        var bodyOpener = LaTeXPreambleBuilder.BuildBodyOpener(doc);
        var bodyCloser = LaTeXPreambleBuilder.BuildBodyCloser(doc);
        if (!string.IsNullOrEmpty(bodyOpener))
        {
            sb.AppendLine(bodyOpener);
            sb.AppendLine();
        }
        sb.AppendLine(BlocksToLatex(mainBlocks));
        if (!string.IsNullOrEmpty(bodyCloser))
        {
            sb.AppendLine();
            sb.AppendLine(bodyCloser);
        }

        // Bibliography
        if (bibEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("% Bibliography");
            sb.AppendLine($@"\bibliographystyle{{{BibStyleName(options.BibliographyStyle, usesNatbib)}}}");
            sb.AppendLine(@"\bibliography{references}");
        }

        sb.AppendLine();
        sb.AppendLine(@"\end{document}");

        files.Add(new ProjectFile("main.tex", sb.ToString()));

        if (bibEntries.Count > 0)
            files.Add(new ProjectFile("references.bib", GenerateBibTeXFile(bibEntries)));

        files.Add(new ProjectFile("README.txt", GenerateReadme(doc.Title)));

        return files;
    }

    // ── Multi-file flat structure ──────────────────────────────────────

    private List<ProjectFile> GenerateMultiFileFlat(
        Document doc,
        List<Block> blocks,
        List<BibliographyEntry> bibEntries,
        LaTeXExportOptions options)
    {
        UseHeadingClass(doc, options);
        var files = new List<ProjectFile>();
        var chapters = GroupBlocksByHeading(blocks);
        var chapterFilenames = new List<string>();

        for (var i = 0; i < chapters.Count; i++)
        {
            var filename = $"chapter-{(i + 1).ToString().PadLeft(2, '0')}.tex";
            var content = BlocksToLatex(chapters[i]);
            files.Add(new ProjectFile($"chapters/{filename}", content));
            chapterFilenames.Add(filename);
        }

        // main.tex
        var main = new StringBuilder();
        var usesNatbib = DocumentUsesNatbib(blocks);
        main.AppendLine(BuildDocumentClassDirective(doc, options));
        main.AppendLine();
        main.AppendLine("% Include preamble");
        main.AppendLine(@"\input{preamble}");
        main.AppendLine();
        main.AppendLine("% Document info");
        AppendTitleMeta(main, doc, blocks);
        main.AppendLine();
        main.AppendLine(@"\begin{document}");
        main.AppendLine();
        main.AppendLine(@"\maketitle");
        main.AppendLine();

        // Abstract
        var abstractBlock = LeadAbstract(blocks);
        if (abstractBlock != null)
        {
            var text = GetContentText(abstractBlock);
            main.AppendLine(@"\begin{abstract}");
            main.AppendLine(FormatInlineContent(text));
            main.AppendLine(@"\end{abstract}");
            main.AppendLine();
        }

        // Table of contents for reports/books
        if (options.DocumentClass != "article")
        {
            main.AppendLine(@"\tableofcontents");
            main.AppendLine(@"\newpage");
            main.AppendLine();
        }

        // Include chapters
        main.AppendLine("% Chapters");
        foreach (var filename in chapterFilenames)
        {
            var name = filename.Replace(".tex", "");
            main.AppendLine($@"\input{{chapters/{name}}}");
        }

        if (bibEntries.Count > 0)
        {
            main.AppendLine();
            main.AppendLine("% Bibliography");
            main.AppendLine($@"\bibliographystyle{{{BibStyleName(options.BibliographyStyle, usesNatbib)}}}");
            main.AppendLine(@"\bibliography{references}");
        }

        main.AppendLine();
        main.AppendLine(@"\end{document}");

        files.Add(new ProjectFile("main.tex", main.ToString()));
        files.Add(new ProjectFile("preamble.tex", GeneratePreambleFile(doc, options, usesNatbib, blocks)));

        if (bibEntries.Count > 0)
            files.Add(new ProjectFile("references.bib", GenerateBibTeXFile(bibEntries)));

        files.Add(new ProjectFile("README.txt", GenerateReadme(doc.Title, true)));

        return files;
    }

    // ── Multi-file Overleaf structure ──────────────────────────────────

    private List<ProjectFile> GenerateMultiFileOverleaf(
        Document doc,
        List<Block> blocks,
        List<BibliographyEntry> bibEntries,
        LaTeXExportOptions options)
    {
        UseHeadingClass(doc, options);
        var files = new List<ProjectFile>();
        var chapters = GroupBlocksByHeading(blocks);
        var chapterInputPaths = new List<string>();

        // Abstract as frontmatter
        var abstractBlock = LeadAbstract(blocks);
        if (abstractBlock != null)
        {
            var text = GetContentText(abstractBlock);
            var abstractContent = string.Join("\n", new[]
            {
                @"\begin{abstract}",
                FormatInlineContent(text),
                @"\end{abstract}"
            });
            files.Add(new ProjectFile("frontmatter/abstract.tex", abstractContent));
        }

        // Per-chapter directories
        for (var i = 0; i < chapters.Count; i++)
        {
            var dirName = $"chap{i + 1}";
            var content = BlocksToLatex(chapters[i]);
            files.Add(new ProjectFile($"{dirName}/chapter.tex", content));
            chapterInputPaths.Add($"{dirName}/chapter");
        }

        // main.tex
        var main = new StringBuilder();
        var usesNatbib = DocumentUsesNatbib(blocks);
        main.AppendLine(BuildDocumentClassDirective(doc, options));
        main.AppendLine();
        main.AppendLine("% Include preamble");
        main.AppendLine(@"\input{preamble}");
        main.AppendLine();
        main.AppendLine("% Document info");
        AppendTitleMeta(main, doc, blocks);
        main.AppendLine();
        main.AppendLine(@"\begin{document}");
        main.AppendLine();
        main.AppendLine(@"\maketitle");
        main.AppendLine();

        if (abstractBlock != null)
        {
            main.AppendLine(@"\input{frontmatter/abstract}");
            main.AppendLine();
        }

        if (options.DocumentClass != "article")
        {
            main.AppendLine(@"\tableofcontents");
            main.AppendLine(@"\newpage");
            main.AppendLine();
        }

        main.AppendLine("% Chapters");
        foreach (var inputPath in chapterInputPaths)
        {
            main.AppendLine($@"\input{{{inputPath}}}");
        }

        if (bibEntries.Count > 0)
        {
            main.AppendLine();
            main.AppendLine("% Bibliography");
            main.AppendLine($@"\bibliographystyle{{{BibStyleName(options.BibliographyStyle, usesNatbib)}}}");
            main.AppendLine(@"\bibliography{references}");
        }

        main.AppendLine();
        main.AppendLine(@"\end{document}");

        files.Add(new ProjectFile("main.tex", main.ToString()));
        files.Add(new ProjectFile("preamble.tex", GeneratePreambleFile(doc, options, usesNatbib, blocks)));

        if (bibEntries.Count > 0)
            files.Add(new ProjectFile("references.bib", GenerateBibTeXFile(bibEntries)));

        files.Add(new ProjectFile("README.txt", GenerateReadme(doc.Title, true, "overleaf")));

        return files;
    }

    // ── Package/preamble generation ────────────────────────────────────

    // Document-class allow-list, article-known options, and the
    // CleanClassOption helper used to live here. They were consolidated
    // into LaTeXPreambleBuilder under LILIA-120 so the export and
    // live-preview paths stay in lock-step. Keep DefaultPreamblePackages
    // here — it's specific to the imported-package emission path that
    // export owns, not class-directive logic.

    // Packages our default preamble already loads OR packages that conflict
    // with it (typeface-swappers redefining math commands that amsmath owns).
    // We must skip imported packages with these names to avoid LaTeX's
    // "Option clash for package X" or "Command \iint already defined" errors.
    /// <summary>
    /// Test seam. Adding a package to the preamble without adding it here is a
    /// failure that only shows up on documents importing the same package, so
    /// it is worth being able to assert the pair stays in step.
    /// </summary>
    internal static bool IsDefaultPreamblePackageForTest(string package) =>
        DefaultPreamblePackages.Contains(package);

    // Lazy, not a plain field initialiser. Static fields initialise in
    // declaration order, so building this eagerly read RefusedForOtherReasons
    // below while it was still null. Deferring it removes the ordering
    // dependency entirely rather than relying on nobody moving a declaration.
    private static readonly Lazy<HashSet<string>> LazyDefaultPreamblePackages =
        new(BuildDefaultPreamblePackages);

    private static HashSet<string> DefaultPreamblePackages => LazyDefaultPreamblePackages.Value;

    /// <summary>
    /// The packages an imported document must not be allowed to load again.
    /// </summary>
    /// <remarks>
    /// Two different reasons, only one of which can be derived. What we already
    /// load is read straight out of the preamble, so adding a package there is
    /// now a single edit and this set cannot fall behind it. What we refuse for
    /// other reasons has to stay written down, because a preamble that never
    /// mentions those packages cannot tell you about them.
    /// </remarks>
    private static HashSet<string> BuildDefaultPreamblePackages()
    {
        var packages = new HashSet<string>(LaTeXPreamble.LoadedPackageNames, StringComparer.OrdinalIgnoreCase);
        packages.UnionWith(RefusedForOtherReasons);
        return packages;
    }

    private static readonly string[] RefusedForOtherReasons =
    [
        // Emitted conditionally by LaTeXPreambleBuilder when the document sets
        // margins, so it appears in no preamble constant and cannot be derived.
        // A document that sets margins AND imports geometry would otherwise get
        // it twice and abort on an option clash.
        "geometry",
        // Typeface / math-font / symbol packages that redefine commands
        // owned by amsmath (\iint, \iiint, etc.). Letting any of these load
        // alongside our defaults triggers "already defined" aborts.
        "newtxtext", "newtxmath", "mathptmx", "txfonts", "pxfonts",
        "mathpazo", "fourier", "libertine", "palatino", "utopia",
        "charter", "cmbright", "kpfonts", "eulervm",
        "wasysym", "mathabx", "stix", "stix2", "times",
        // XeTeX / LuaTeX-only font loaders — our pdflatex container can't
        // use them. Load lmodern-based defaults instead.
        "fontspec", "unicode-math", "polyglossia",
        // newspaper (Overleaf newspaper template) requires yfonts.sty (Fraktur)
        // which isn't on the default texlive install. We skip the package and
        // provide no-op shims for its macros in LaTeXPreamble.NewspaperShims.
        "newspaper", "yfonts"
    ];

    /// <summary>
    /// Build the `\documentclass[...]{...}` directive. Thin wrapper over
    /// <see cref="LaTeXPreambleBuilder.BuildClassDirective"/>; kept here
    /// only as a stable entry point for the export call sites. The
    /// builder is the single source of truth — see LILIA-120.
    /// </summary>
    private static string BuildDocumentClassDirective(Document doc, LaTeXExportOptions options)
    {
        return LaTeXPreambleBuilder.BuildClassDirective(
            doc,
            fontSizeOverride: string.IsNullOrEmpty(options.FontSize) ? null : options.FontSize,
            paperSizeOverride: string.IsNullOrEmpty(options.PaperSize) ? null : options.PaperSize,
            fallbackClass: options.DocumentClass);
    }

    /// <summary>
    /// Emit `\usepackage{...}` lines for imported preamble packages that
    /// aren't already in our default preamble. Wraps each in \IfFileExists so
    /// a missing .sty on the container degrades to a no-op instead of aborting
    /// compilation. JSON shape: [{ "name": "...", "options": "..." }].
    /// </summary>
    private static string BuildImportedPackageLines(Document doc)
    {
        if (string.IsNullOrWhiteSpace(doc.LatexPackages)) return string.Empty;
        try
        {
            using var json = JsonDocument.Parse(doc.LatexPackages);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;
            // LILIA-130: emit the comment header only if at least one
            // package actually goes into the body. See RenderService for
            // the same fix; both paths shared the original behavior of
            // emitting the comment unconditionally.
            var body = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pkg in json.RootElement.EnumerateArray())
            {
                var name = pkg.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (DefaultPreamblePackages.Contains(name)) continue; // avoid option clash
                if (!emitted.Add(name)) continue;
                var opts = pkg.TryGetProperty("options", out var o) ? o.GetString() : null;
                var load = string.IsNullOrWhiteSpace(opts)
                    ? $"\\usepackage{{{name}}}"
                    : $"\\usepackage[{opts}]{{{name}}}";
                body.AppendLine($"\\IfFileExists{{{name}.sty}}{{{load}}}{{}}");
            }
            if (body.Length == 0) return string.Empty;
            return "% Packages preserved from imported preamble (IfFileExists-wrapped)"
                 + Environment.NewLine
                 + body.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GeneratePreambleFile(Document doc, LaTeXExportOptions options, bool usesNatbib = false,
        IEnumerable<Block>? bodyBlocks = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("% Preamble file - included by main.tex");
        sb.AppendLine();
        sb.Append(GeneratePackageLines(doc, options, usesNatbib, bodyBlocks));
        return sb.ToString();
    }

    private string GeneratePackageLines(Document doc, LaTeXExportOptions options, bool usesNatbib = false,
        IEnumerable<Block>? bodyBlocks = null)
    {
        var sb = new StringBuilder();

        // Imported packages first — user's class often needs them before our defaults.
        var importedPkgs = BuildImportedPackageLines(doc);
        if (!string.IsNullOrEmpty(importedPkgs))
        {
            sb.Append(importedPkgs);
            sb.AppendLine();
        }

        // natbib — only when the document actually uses \citep/\citet/etc.
        // (paired with the natbib-compatible \bibliographystyle in
        // BibStyleName). Loaded BEFORE the shared packages so it precedes
        // hyperref (the recommended order). Skipped for legacy \cite-only
        // docs so their numeric \bibliographystyle{plain} output is unchanged.
        if (usesNatbib)
        {
            sb.AppendLine("% natbib — author-year / textual citations (\\citet, \\citep, …)");
            sb.AppendLine(@"\usepackage{natbib}");
            sb.AppendLine();
        }

        // Use the shared preamble — same 31 packages as validation
        sb.Append(LaTeXPreamble.ForClass(LaTeXPreamble.Packages, LaTeXPreambleBuilder.ResolveClassName(doc, options.DocumentClass)));
        sb.AppendLine();

        // Chinese, Japanese or Korean text: compiled with xelatex (MCP-26, EngineDetector reads
        // the xeCJK line), so the file says so for whoever compiles it next.
        var bodyForCjk = bodyBlocks ?? doc.Blocks ?? new List<Block>();
        if (CjkText.Contains(doc.Title) || bodyForCjk.Any(b => CjkText.Contains(b.Content.RootElement)))
        {
            sb.AppendLine("% Chinese / Japanese / Korean text: compile with xelatex.");
            sb.Append(LaTeXPreamble.EngineAddendum(LatexEngine.Xelatex, cjk: true));
            sb.AppendLine();
        }

        // TikZ / pgfplots / tikz-cd, only when the document has TikZ figures and does not load them itself.
        var tikzPackages = TikzFigure.PackageLines(
            (bodyBlocks ?? doc.Blocks ?? new List<Block>()).Select(b => (b.Type, b.Content.RootElement)),
            doc.LatexPackages, doc.CustomPreamble);
        if (tikzPackages.Length > 0)
        {
            sb.Append(tikzPackages);
            sb.AppendLine();
        }

        // Shims so journal-class-specific commands in imported bodies
        // (\begin{keywords}, \affiliation, etc.) don't abort compilation.
        sb.Append(LaTeXPreamble.JournalShims);
        sb.Append(LaTeXPreamble.CvShims);
        sb.Append(LaTeXPreamble.BeamerShims);
        sb.Append(LaTeXPreamble.NewspaperShims);
        sb.Append(LaTeXPreamble.CalendarShims);
        sb.AppendLine();

        // Graphics path for figures
        sb.AppendLine(@"\graphicspath{{./figures/}}");
        sb.AppendLine();

        // Listings defaults
        sb.AppendLine(@"\lstset{");
        sb.AppendLine(@"  basicstyle=\ttfamily\small,");
        sb.AppendLine(@"  breaklines=true,");
        sb.AppendLine(@"  frame=single,");
        sb.AppendLine(@"  numbers=left,");
        sb.AppendLine(@"  numberstyle=\tiny,");
        sb.AppendLine(@"}");

        // Shared theorem environments
        sb.AppendLine();
        sb.Append(LaTeXPreamble.TheoremEnvironmentsFor(LaTeXPreambleBuilder.ResolveClassName(doc, options.DocumentClass)));

        // Optional domain-specific packages
        if (options.IncludePhysics)
        {
            sb.AppendLine();
            sb.AppendLine("% Physics notation");
            sb.AppendLine(@"\usepackage{physics}");
        }

        // mhchem when the text uses \ce (LaTeXPreamble.MhchemLine), as the check loads it.
        if (!options.IncludeChemistry)
            sb.Append(LaTeXPreamble.MhchemLine(
                (bodyBlocks ?? doc.Blocks ?? new List<Block>()).Select(b => b.Content.RootElement.ToString()),
                doc.CustomPreamble, doc.LatexPackages));

        if (options.IncludeChemistry)
        {
            sb.AppendLine();
            sb.AppendLine("% Chemistry formulas");
            sb.AppendLine(@"\usepackage[version=4]{mhchem}");
        }

        // Layout settings (margins, line spacing, paragraph indent, page
        // numbering, header/footer, font family, column gap/separator,
        // multicol package). All routed through the consolidated builder
        // so export and live-render stay in sync — see LILIA-120.
        var layout = LaTeXPreambleBuilder.BuildLayoutPreamble(
            doc,
            // Honour the legacy options.LineSpacing override only when
            // the document itself doesn't set a line-spacing value;
            // otherwise the document's stored setting wins.
            lineSpacingOverride: doc.LineSpacing.HasValue ? null : options.LineSpacing);
        if (!string.IsNullOrWhiteSpace(layout))
        {
            sb.AppendLine();
            sb.Append(layout);
        }

        // The document theme (Document settings → Look): one managed line just before the custom
        // preamble, so the author's settings win. Written as stored for a download (it compiles
        // wherever the theme's fonts are, Overleaf included); a compile here (the PDF export)
        // asks for ThemeUse.Compile and fails on a theme this server cannot print.
        var themeLine = LaTeXPreambleBuilder.BuildThemeLine(
            doc, bodyBlocks ?? doc.Blocks?.OrderBy(b => b.SortOrder), options.LookOverride,
            options.CompileHere ? LaTeXPreambleBuilder.ThemeUse.Compile : LaTeXPreambleBuilder.ThemeUse.Export);
        if (!string.IsNullOrEmpty(themeLine))
        {
            sb.AppendLine();
            sb.Append(themeLine);
        }

        // User-authored custom preamble (macros / environments) — emitted last,
        // after all packages + layout, so the author's \newcommand /
        // \newenvironment / \DeclareMathOperator can build on everything loaded
        // above and override defaults. Verbatim; the author owns its validity.
        if (!string.IsNullOrWhiteSpace(doc.CustomPreamble))
        {
            sb.AppendLine();
            sb.AppendLine("% Custom preamble (author-defined macros)");
            sb.AppendLine(LaTeXPreamble.CustomPreambleForEmit(doc.CustomPreamble));
        }

        return sb.ToString();
    }

    // ── Block rendering ────────────────────────────────────────────────

    /// <summary>
    /// Drop the leading heading block when its text matches the doc
    /// title (case-insensitive, normalized whitespace). Pairs with
    /// the unconditional \title{X}+\maketitle in the preamble — without
    /// this, LaTeX-imported docs render the title twice.
    /// </summary>
    /// <summary>
    /// Emit \title/\author/\date for the export preamble. Sources them from a
    /// Title block if present (its title doubles as the document name), else
    /// the document title with empty author and \today — matching the preview
    /// render path (RenderService.ResolveTitleMeta).
    /// </summary>
    private static void AppendTitleMeta(StringBuilder sb, Document doc, List<Block> blocks)
    {
        string title = doc.Title ?? "", author = "", date = @"\today";
        var titleBlock = blocks.FirstOrDefault(b =>
            string.Equals(b.Type, BlockTypes.Title, StringComparison.OrdinalIgnoreCase));
        if (titleBlock?.Content != null && titleBlock.Content.RootElement.ValueKind == JsonValueKind.Object)
        {
            var root = titleBlock.Content.RootElement;
            var t = root.TryGetProperty("title", out var tp) ? tp.GetString() : null;
            var a = root.TryGetProperty("author", out var ap) ? ap.GetString() : null;
            var d = root.TryGetProperty("date", out var dp) ? dp.GetString() : null;
            if (!string.IsNullOrWhiteSpace(t)) title = t!;
            if (!string.IsNullOrWhiteSpace(a)) author = a!;
            // Keep \today / free text / LaTeX tokens via FormatTitleMetaLatex
            // (bare EscapeLatex turns \and / \today into \textbackslash{}).
            if (!string.IsNullOrWhiteSpace(d)) date = FormatTitleMetaLatex(d!);
        }
        sb.AppendLine($@"\title{{{FormatTitleMetaLatex(RenderService.TitleWithoutAnd(title))}}}");
        sb.AppendLine($@"\author{{{Lilia.Engines.AuthorLatex.For(author, FormatTitleMetaLatex)}}}");
        sb.AppendLine($@"\date{{{date}}}");
    }

    /// <summary>
    /// Escape free text in \title/\author/\date while preserving common
    /// LaTeX title-meta tokens (<c>\and</c>, <c>\\</c>, <c>\today</c>,
    /// <c>\thanks{…}</c>). Shared semantics with RenderService.
    /// </summary>
    internal static string FormatTitleMetaLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var placeholders = new List<string>();
        string Ph(string s)
        {
            placeholders.Add(s);
            return $"\x00TM{placeholders.Count - 1}\x00";
        }

        var result = text;
        result = Regex.Replace(result, @"\\thanks\{([^{}]*)\}",
            m => Ph($@"\thanks{{{EscapeLatex(m.Groups[1].Value)}}}"));
        result = Regex.Replace(result, @"\\and\b", _ => Ph(@"\and"));
        result = Regex.Replace(result, @"\\today\b", _ => Ph(@"\today"));
        result = Regex.Replace(result, @"\\\\", _ => Ph(@"\\"));

        result = EscapeLatex(result);
        result = Regex.Replace(result, @"\x00TM(\d+)\x00",
            m => placeholders[int.Parse(m.Groups[1].Value)]);
        return result;
    }

    /// <summary>
    /// The blocks the body prints, in order: no title, abstract or bibliography block (the
    /// preamble and \maketitle carry those), and no leading heading that repeats the title.
    /// The theme line numbers Index pins by these, and the editor's section chips
    /// (<see cref="Lilia.Engines.Themes.ThemeSections"/>) count the same headings.
    /// </summary>
    public static List<Block> BodyBlocks(string? title, IEnumerable<Block> orderedBlocks, bool abstractInBody = false)
    {
        var all = orderedBlocks.ToList();
        var lead = abstractInBody ? null : LeadAbstract(all);
        return StripDuplicateTitleHeading(title,
            all.Where(b => !ReferenceEquals(b, lead) && b.Type != "bibliography" && b.Type != "title").ToList());
    }

    /// <summary>
    /// The abstract printed after \maketitle: the first one. Any other stays in the body where the
    /// author put it — a second abstract (a translation, say) was dropped from every export
    /// while the check printed it (MCP-39).
    /// </summary>
    private static Block? LeadAbstract(IEnumerable<Block> blocks) => blocks.FirstOrDefault(b => b.Type == "abstract");

    private static List<Block> StripDuplicateTitleHeading(string? title, List<Block> blocks)
    {
        if (string.IsNullOrWhiteSpace(title) || blocks.Count == 0) return blocks;
        var canon = NormalizeTitleForCompare(title);
        // Look for the FIRST heading; non-heading content blocks before
        // it short-circuit (their presence means the title heading
        // isn't the leading element).
        Block? leadingHeading = null;
        foreach (var b in blocks)
        {
            if (string.Equals(b.Type, "heading", StringComparison.OrdinalIgnoreCase))
            {
                leadingHeading = b;
                break;
            }
            if (IsTitleStripContentBlock(b.Type)) return blocks;
        }
        if (leadingHeading == null) return blocks;
        try
        {
            var text = leadingHeading.Content.RootElement.TryGetProperty("text", out var t)
                ? t.GetString() ?? "" : "";
            if (NormalizeTitleForCompare(text) == canon)
                return blocks.Where(b => b.Id != leadingHeading.Id).ToList();
        }
        catch { /* malformed — keep block */ }
        return blocks;
    }

    private static string NormalizeTitleForCompare(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool IsTitleStripContentBlock(string type) => type?.ToLowerInvariant() switch
    {
        "paragraph" or "equation" or "figure" or "table" or "code"
            or "list" or "blockquote" or "theorem" or "abstract"
            or "bibliography" => true,
        _ => false,
    };

    private string BlocksToLatex(List<Block> blocks)
    {
        var rendered = blocks.Select(RenderBlock).ToList();
        // A deck's footnote blocks go inside the slide before them (SlideLatex.AttachFootnotes).
        if (LaTeXPreamble.IsBeamer(_headingClass))
            SlideLatex.AttachFootnotes(blocks.Select(b => b.Type).ToList(), rendered);
        // Composed as the check composes it: book divisions, break attributes, floats in multicols.
        return _doc is null
            ? string.Join("\n\n", rendered.Where(r => !string.IsNullOrEmpty(r)))
            : _check.ComposeBody(_doc, blocks, rendered, pageMap: false).TrimEnd();
    }

    private string RenderBlock(Block block)
    {
        try
        {
            var content = block.Content.RootElement;

            // One emitter (run 2, MCP-76): every body block is rendered by the check emitter, so the
            // export prints what check_document judged. The export keeps only what it needs to do
            // differently: figures (files under figures/, with a placeholder when one is missing)
            // and the bibliography (a .bib, with the text references added: WithTextEntries).
            switch (block.Type)
            {
                case "figure" or "image":
                    return RenderFigure(content);
                case "bibliography":
                    return "";
            }
            if (_doc is not null && block.Document is null) block.Document = _doc;
            var latex = _check.RenderBlockToLatex(block, _longtableAllowed && RenderService.IsLongTable(block));
            return latex.StartsWith("% Unknown block type", StringComparison.Ordinal) ? RenderUnknownBlock(block) : latex;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to LaTeX for export", block.Id);
            return $"% Error rendering block: {block.Id}";
        }
    }

    /// <summary>
    /// Default-case fallback in the export switch — emit a comment
    /// marker so the user sees the block's location and writes a
    /// telemetry row so the dev team sees the silent fallback. Tier 3
    /// of the export defence-in-depth strategy.
    /// </summary>
    private string RenderUnknownBlock(Block block)
    {
        _telemetry.Record(new Lilia.Import.Services.ImportTelemetryRecord
        {
            EventKind = "silent_fallback",
            Severity = "warn",
            SourceFormat = "latex",
            TokenOrEnv = block.Type,
            BlockKindEmitted = "comment",
            SampleText = $"block.id={block.Id}",
        });
        return $"% [Unsupported block type for LaTeX export: {block.Type}]";
    }




    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";




    private string RenderFigure(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
        // span="page" → \begin{figure*}, which in single-column mode behaves
        // identically to \begin{figure}, so we can emit it unconditionally.
        var env = RenderService.IsFullWidth(content) ? "figure*" : "figure";

        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{{LabelKey.Effective("figure", label)}}}" : "";

        // A TikZ figure: its source goes out byte for byte (TikzFigure).
        if (TikzFigure.IsTikz(content))
            return TikzFigure.ToLatex(content, string.IsNullOrEmpty(caption) ? "" : InlineArgument(caption),
                string.IsNullOrEmpty(label) ? "" : LabelKey.Effective("figure", label), env);

        // Subfigures — when the figure carries a `subfigures` array, emit a
        // subcaption layout (subcaption is in the preamble). Each panel gets an
        // equal share of \textwidth, mirroring the editor's own preview. The
        // overall \caption + \label go after the panels.
        if (content.TryGetProperty("subfigures", out var subs)
            && subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0)
        {
            var panels = subs.EnumerateArray().ToList();
            var width = Math.Max(0.2, Math.Min(0.9, 0.9 / panels.Count)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            var fsb = new StringBuilder();
            fsb.AppendLine($@"\begin{{{env}}}[H]");
            fsb.AppendLine(@"\centering");
            for (int i = 0; i < panels.Count; i++)
            {
                var sfSrc = panels[i].TryGetProperty("src", out var ss) ? ss.GetString() ?? "" : "";
                var sfCap = panels[i].TryGetProperty("caption", out var scp) ? scp.GetString() ?? "" : "";
                var sfLabel = panels[i].TryGetProperty("label", out var sl) ? sl.GetString() ?? "" : "";
                fsb.AppendLine($@"\begin{{subfigure}}{{{width}\textwidth}}");
                fsb.AppendLine(@"\centering");
                if (!string.IsNullOrEmpty(sfSrc))
                {
                    var fn = ExtractImageFilename(sfSrc);
                    fsb.AppendLine($@"\IfFileExists{{figures/{fn}}}{{\includegraphics[width=\textwidth]{{figures/{fn}}}}}{{\fbox{{\small\textit{{[Missing: {EscapeLatex(fn)}]}}}}}}");
                }
                if (!string.IsNullOrEmpty(sfCap))
                    fsb.AppendLine($@"\caption{{{EscapeLatex(sfCap)}}}" + (!string.IsNullOrEmpty(sfLabel) ? $@"\label{{{LabelKey.Effective("figure", sfLabel)}}}" : ""));
                fsb.AppendLine(@"\end{subfigure}");
                // a little horizontal gap between panels (not after the last)
                if (i < panels.Count - 1) fsb.AppendLine(@"\hfill");
            }
            if (!string.IsNullOrEmpty(caption))
                fsb.AppendLine($@"\caption{{{InlineArgument(caption)}}}{labelPart}");
            else if (!string.IsNullOrEmpty(labelPart))
                fsb.AppendLine(labelPart);
            fsb.Append($@"\end{{{env}}}");
            return fsb.ToString();
        }

        var sb = new StringBuilder();
        sb.AppendLine($@"\begin{{{env}}}[H]");
        sb.AppendLine(@"\centering");
        if (!string.IsNullOrEmpty(src))
        {
            var filename = ExtractImageFilename(src);
            // Wrap in IfFileExists so a missing asset renders as a labelled
            // placeholder frame instead of aborting compilation with pdftex.def.
            sb.AppendLine($@"\IfFileExists{{figures/{filename}}}{{%");
            sb.AppendLine($@"  \includegraphics[width={FigureWidth.Latex(content)}]{{figures/{filename}}}%");
            sb.AppendLine($@"}}{{\fbox{{\parbox{{0.7\textwidth}}{{\centering\small\textit{{[Missing figure: {EscapeLatex(filename)}]}}}}}}}}");
        }
        else
        {
            sb.AppendLine(@"% [figure placeholder — no image uploaded]");
        }
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($@"\caption{{{InlineArgument(caption)}}}{labelPart}");
        else if (!string.IsNullOrEmpty(labelPart))
            sb.AppendLine(labelPart);
        sb.Append($@"\end{{{env}}}");
        return sb.ToString();
    }



    private static (int Colspan, int Rowspan) TableCellSpan(JsonElement cell)
    {
        if (cell.ValueKind != JsonValueKind.Object) return (1, 1);
        int cs = cell.TryGetProperty("colspan", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 1;
        int rs = cell.TryGetProperty("rowspan", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 1;
        return (Math.Max(1, cs), Math.Max(1, rs));
    }


    /// <summary>
    /// Walks the items array recursively, emitting nested
    /// itemize/enumerate environments inside each <c>\item</c>. Nested
    /// lists inherit the parent env (LaTeX itself permits mixing — e.g.
    /// <c>itemize</c> inside <c>enumerate</c> — but the editor's data
    /// model doesn't currently let users author that, mirroring the
    /// HTML renderer's inheritance). Indentation is cosmetic.
    /// </summary>
    private void AppendListItems(JsonElement items, string env, int depth, StringBuilder sb, List<string>? enumOptions = null)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        var indent = new string(' ', depth * 2);
        // enumitem options apply only to the outermost env (depth 0). Nested
        // children inherit defaults — same shape as RenderService.
        if (depth == 0 && enumOptions != null && enumOptions.Count > 0)
        {
            sb.AppendLine($@"{indent}\begin{{{env}}}[{string.Join(", ", enumOptions)}]");
        }
        else
        {
            sb.AppendLine($@"{indent}\begin{{{env}}}");
        }
        var isDescription = env == "description";
        foreach (var item in items.EnumerateArray())
        {
            var itemText = ExtractItemText(item);
            if (isDescription)
            {
                // Description list: emit `\item[<term>] <description>`.
                // The term goes in square brackets (LaTeX renders bold).
                string descriptionText = "";
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("description", out var descProp)
                    && descProp.ValueKind == JsonValueKind.String)
                {
                    descriptionText = descProp.GetString() ?? "";
                }
                sb.AppendLine($@"{indent}\item[{FormatInlineContent(itemText)}] {FormatInlineContent(descriptionText)}");
            }
            else
            {
                sb.AppendLine($@"{indent}\item {FormatInlineContent(itemText)}");
            }
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("children", out var children) &&
                children.ValueKind == JsonValueKind.Array &&
                children.GetArrayLength() > 0)
            {
                AppendListItems(children, env, depth + 1, sb);
            }
        }
        sb.AppendLine($@"{indent}\end{{{env}}}");
    }


    /// <summary>A caption or theorem title: maths and marks as in a paragraph, on one line (MCP-14/60).</summary>
    private string InlineArgument(string? text) =>
        FormatInlineContent(Regex.Replace(text ?? "", @"\s*\r?\n\s*", " ")).Trim();



    // ── Inline content formatting (placeholder approach) ───────────────

    internal static string FormatInlineContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var placeholders = new List<string>();

        string Ph(string latex)
        {
            var idx = placeholders.Count;
            placeholders.Add(latex);
            return $"\x00PH{idx}\x00";
        }

        var result = text;
        // MCP-16: a price ($5, $10) is not the start of a formula (CurrencyDollar).
        result = Lilia.Engines.CurrencyDollar.Protect(result, Ph);

        // 1a. Display math: $$x^2$$ → pass through as-is. MUST run
        // before single-$ rule so the outer pair isn't mistaken for
        // two adjacent inline-math spans.
        result = Regex.Replace(result, @"\$\$([^$]+)\$\$", m => Ph($"$${m.Groups[1].Value}$$"));

        // 1b. Inline math: $x^2$ → pass through as-is
        result = Regex.Replace(result, @"\$([^$]+)\$", m => Ph($"${m.Groups[1].Value}$"));

        // 1b'. LaTeX-native math delimiters: \[ ... \] (display) and \( ... \) (inline).
        // Without this the EscapeLatex pass turns the backslashes into
        // \textbackslash{} and the LML superscript regex below mangles
        // every "^…^" pair inside the math — producing things like
        //   \[ x\textsuperscript{2 + y}2 = z\textasciicircum{}2 \]
        // from a paragraph that originally said "\[ x^2 + y^2 = z^2 \]".
        // Treat the whole math span as opaque; if it really is a math
        // expression the user wrote, the LaTeX compiler handles ^ and _
        // correctly. If it isn't, FormatInlineContent isn't the place
        // to disambiguate.
        result = Regex.Replace(result, @"\\\[([\s\S]+?)\\\]", m => Ph($@"\[{m.Groups[1].Value}\]"), RegexOptions.Singleline);
        result = Regex.Replace(result, @"\\\(([\s\S]+?)\\\)", m => Ph($@"\({m.Groups[1].Value}\)"), RegexOptions.Singleline);

        // 1b''. Body date tokens. Preamble `\date{…}` mid-paragraph is a
        // no-op — rewrite to printable `\today` / free text. Bare `\today`
        // / `\and` must also survive EscapeLatex.
        result = Regex.Replace(result, @"\\date\{\\today\}", _ => Ph(@"\today"));
        result = Regex.Replace(result, @"\\date\{([^}]*)\}", m =>
        {
            var inner = (m.Groups[1].Value ?? "").Trim();
            if (string.IsNullOrEmpty(inner) || inner == @"\today") return Ph(@"\today");
            return inner; // free text → escape pass below
        });
        result = Regex.Replace(result, @"\\today\b", _ => Ph(@"\today"));
        result = Regex.Replace(result, @"\\and\b", _ => Ph(@"\and"));

        // 1c. Native LaTeX commands users type directly (\cite, \ref,
        //     \eqref, \url, \href, \label, \footnote). Without these,
        //     EscapeLatex turns "\cite{X}" into "\textbackslash{}cite\{X\}"
        //     which renders as literal text in the PDF instead of a
        //     resolved citation. The placeholder preserves the command
        //     verbatim through the escape pass.
        // Citation family — preserve the EXACT command (cite / citep / citet /
        // citeauthor / citeyear) through the escape pass. Group 1 captures the
        // command so natbib modes aren't flattened back to \cite.
        result = Regex.Replace(result, @"\\(cite(?:p|t|author|year)?)\{([^}]+)\}", m => Ph($@"\{m.Groups[1].Value}{{{m.Groups[2].Value}}}"));
        // Reference family — preserve the exact command, as the cite family does.
        // This used to know \ref and \eqref only, so \cref — what the editor's @
        // picker writes, and cleveref is in the preamble — was escaped to
        // \textbackslash{}cref and printed as raw text. Same set the editor
        // round-trips and the reference index recognises.
        result = Regex.Replace(result, @"\\(ref|eqref|cref|Cref|autoref|pageref|nameref)\{([^}]+)\}",
            m => Ph($@"\{m.Groups[1].Value}{{{m.Groups[2].Value}}}"));
        result = Regex.Replace(result, @"\\url\{([^}]+)\}", m => Ph($@"\url{{{m.Groups[1].Value}}}"));
        // \href two-arg form first, then single-arg fallback (treat as bare link).
        result = Regex.Replace(result, @"\\href\{([^}]+)\}\{([^}]+)\}", m => Ph($@"\href{{{m.Groups[1].Value}}}{{{m.Groups[2].Value}}}"));
        result = Regex.Replace(result, @"\\href\{([^}]+)\}", m => Ph($@"\url{{{m.Groups[1].Value}}}"));
        result = Regex.Replace(result, @"\\label\{([^}]+)\}", m => Ph($@"\label{{{m.Groups[1].Value}}}"));
        result = Regex.Replace(result, @"\\footnote\{([^}]+)\}", m => Ph($@"\footnote{{{m.Groups[1].Value}}}"));

        // 1d. FormatRail-serialised LaTeX commands — `\textcolor{...}{...}`,
        //     `\hl[color]{...}` / `\hl{...}`, and the `{\large …}` size
        //     group form. These are the commands the editor's color /
        //     highlight / size pickers emit when serialising marks.
        //     Without explicit pass-through they get backslash-escaped to
        //     literal text in the export PDF (user reported 2026-05-14).
        result = Regex.Replace(result, @"\\textcolor\{([^{}]+)\}\{([^{}]+)\}",
            m => Ph($@"\textcolor{{{m.Groups[1].Value}}}{{{EscapeLatex(m.Groups[2].Value)}}}"));
        result = Regex.Replace(result, @"\\hl(?:\[([^\]]+)\])?\{([^{}]+)\}",
            m =>
            {
                var color = m.Groups[1].Success ? m.Groups[1].Value : null;
                var inner = EscapeLatex(m.Groups[2].Value);
                return Ph(color != null ? $@"\hl[{color}]{{{inner}}}" : $@"\hl{{{inner}}}");
            });
        result = Regex.Replace(result,
            @"\{\\(tiny|scriptsize|footnotesize|small|normalsize|large|Large|LARGE|huge|Huge)\s+([^{}]+)\}",
            m => Ph($@"{{\{m.Groups[1].Value} {EscapeLatex(m.Groups[2].Value)}}}"));

        // 1e. Comment marker `[%…%]` — pick form by content shape.
        //     Same idiom as RenderService.ProcessLatexText so the export
        //     PDF, the LaTeX preview, and the Typst PDF all render
        //     comments identically (hidden from output). Multi-line uses
        //     \begin{comment}…\end{comment} (requires the `comment`
        //     package, loaded by LaTeXPreamble.Packages); single-line
        //     uses the TeX primitive \iffalse…\fi.
        // Always emit \iffalse…\fi — the comment-package env errors
        // when used mid-paragraph (see RenderService comment).
        result = Regex.Replace(result, @"\[%([\s\S]+?)%\]",
            m => Ph($"\\iffalse {m.Groups[1].Value}\\fi{{}}"));

        // 1h. Vertical-skip commands need `\par` wrappers to actually
        //     produce visible vertical space — mid-paragraph skips are
        //     queued silently otherwise. Mirror of RenderService logic.
        result = Regex.Replace(result, @"\\(smallskip|medskip|bigskip|vfill)\b(?:\{\})?",
            m => Ph($"\\par\\{m.Groups[1].Value}\\par "));
        result = Regex.Replace(result, @"\\vspace\*?\{([^}]+)\}",
            m => Ph($"\\par\\vspace{{{m.Groups[1].Value}}}\\par "));

        // 1f. LML inline marks the editor serialises (smallcaps `^^…^^`,
        //     superscript `^…^`, subscript `%%…%%`, strikethrough
        //     `~~…~~`). Smallcaps before sup so `^^X^^` isn't grabbed by
        //     the sup regex. Strikethrough before the `~` escape would
        //     mangle it.
        result = Regex.Replace(result, @"\^\^([^^]+)\^\^",
            m => Ph($@"\textsc{{{EscapeLatex(m.Groups[1].Value)}}}"));
        result = Regex.Replace(result, @"%%([^%]+)%%",
            m => Ph($@"\textsubscript{{{EscapeLatex(m.Groups[1].Value)}}}"));
        result = Regex.Replace(result, @"(?<!\^)\^([^^\s][^^]*?)\^(?!\^)",
            m => Ph($@"\textsuperscript{{{EscapeLatex(m.Groups[1].Value)}}}"));
        result = Regex.Replace(result, @"~~([^~]+)~~",
            m => Ph($@"\st{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 1g. Yellow highlight markdown `==…==` → \hl{...}
        result = Regex.Replace(result, @"==([^=]+)==",
            m => Ph($@"\hl{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 2. Inline code: `text` → \texttt{text}
        result = Regex.Replace(result, @"`([^`]+)`", m => Ph($@"\texttt{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 2b. Underline: __text__ → \underline{text}. The editor serialises its
        //     underline mark as __text__ (content-converter.ts) and the preview
        //     renderer maps it (RenderService). Without this rule the italic
        //     _text_ rule below took the inner pair, exporting
        //     "\_\textit{text}\_".
        result = Regex.Replace(result, @"__([^_]+)__", m => Ph($@"\underline{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 3. Bold: **text** → \textbf{text} (canonical markdown form;
        //    matches what tiptapToBlockContent emits + sanitizeToHtml
        //    on the preview side. Pre-fix the exporter only handled
        //    single `*` for bold which inverted the convention.)
        result = Regex.Replace(result, @"\*\*([^*]+)\*\*", m => Ph($@"\textbf{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 4. Italic: *text* → \textit{text} (single asterisk is italic
        //    per markdown convention).
        result = Regex.Replace(result, @"(?<!\*)\*([^*]+)\*(?!\*)", m => Ph($@"\textit{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 5. Italic alias: _text_ → \textit{text} (BlockRenderer accepts
        //    both forms; exporter aligns).
        // Word-boundary underscores only. Without the guards this matched
        // inside identifiers: "the value_of_x" became "the value\textit{of}x",
        // losing both underscores and inventing italics in the middle of a
        // name. CommonMark declines intraword underscores for exactly this
        // reason — snake_case is far more common in a technical paper than
        // _emphasis_ is. Standalone _emphasis_ still italicises.
        result = Regex.Replace(
            result,
            @"(?<![A-Za-z0-9\\])_([^_]+)_(?![A-Za-z0-9])",
            m => Ph($@"\textit{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 5–6. Lilia's shorthands: Kind~(label), @ref{label}, @cite{key} — the same rules as the
        // compile emitter (Lilia.Engines.CrossRefSyntax), so an export reads like the PDF.
        result = Lilia.Engines.CrossRefSyntax.Resolve(result, Ph);
        // \end{document} typed in text would end the export there (MCP-52): print it instead.
        result = Lilia.Engines.DocumentFence.Neutralize(result, Ph);

        // 7. Escape remaining plain text; a ~ between two characters stays a tie (MCP-67).
        // MCP-09: LaTeX the author typed in prose stays LaTeX, as the check emitter keeps it —
        // commands, control symbols and braces pass; the text between them is still escaped.
        // \emph{…}, \ce{…}, \SI{…}{…} printed as \textbackslash{}emph… in every export.
        result = Regex.Replace(result, @"\\(?:[A-Za-z]+\*?|[%&#_{}$,;:! \\])|[{}]", m => Ph(m.Value));

        result = Lilia.Engines.TieSyntax.Protect(result, Ph);
        result = EscapeLatex(result);

        // 8. Restore placeholders
        // A placeholder can sit inside another's text (a price inside a formula), so restore until
        // none is left; each pass only goes down a level, and nesting is shallow.
        for (var pass = 0; pass < 8 && result.Contains("\x00PH", StringComparison.Ordinal); pass++)
            result = Regex.Replace(result, @"\x00PH(\d+)\x00", m => placeholders[int.Parse(m.Groups[1].Value)]);

        return result;
    }

    // ── LaTeX escaping ─────────────────────────────────────────────────

    internal static string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        return LatexText.Escape(text);
    }

    // ── Image filename extraction ──────────────────────────────────────

    internal static string ExtractImageFilename(string src, int index = 0)
    {
        if (string.IsNullOrEmpty(src)) return $"image-{index}.png";

        var parts = src.Split('/');
        var filename = parts.Length > 0 ? parts[^1] : $"image-{index}";

        // Remove query params and hash
        filename = filename.Split('?')[0].Split('#')[0];

        // Ensure valid image extension
        if (!Regex.IsMatch(filename, @"\.(jpg|jpeg|png|gif|webp|svg|bmp|tiff?|pdf|eps)$", RegexOptions.IgnoreCase))
            filename = $"image-{index}.png";

        // Sanitize for LaTeX
        filename = Regex.Replace(filename, @"\s+", "-");
        filename = Regex.Replace(filename, @"[^a-zA-Z0-9._-]", "-");
        filename = Regex.Replace(filename, @"-+", "-");
        filename = filename.ToLowerInvariant();

        return filename;
    }

    // ── BibTeX generation ──────────────────────────────────────────────
    // Serialization moved to BibTeXSerializer so the Typst preview path
    // can reuse the same .bib output alongside main.typ.

    private string GenerateBibTeXFile(List<BibliographyEntry> entries) =>
        BibTeXSerializer.Serialize(entries);

    // ── Chapter splitting ──────────────────────────────────────────────

    private static List<List<Block>> GroupBlocksByHeading(List<Block> blocks)
    {
        var lead = LeadAbstract(blocks);
        var chapters = new List<List<Block>>();
        var currentChapter = new List<Block>();

        foreach (var block in blocks)
        {
            if (block.Type == "heading")
            {
                var level = 1;
                try
                {
                    if (block.Content.RootElement.TryGetProperty("level", out var l))
                        level = l.GetInt32();
                }
                catch { }

                if (level <= 2)
                {
                    if (currentChapter.Count > 0)
                        chapters.Add(currentChapter);
                    currentChapter = new List<Block> { block };
                    continue;
                }
            }

            if (!ReferenceEquals(block, lead) && block.Type != "bibliography")
                currentChapter.Add(block);
        }

        if (currentChapter.Count > 0)
            chapters.Add(currentChapter);

        // If no headings, put all blocks in one chapter
        if (chapters.Count == 0 && blocks.Count > 0)
        {
            chapters.Add(blocks.Where(b => !ReferenceEquals(b, lead) && b.Type != "bibliography").ToList());
        }

        return chapters;
    }

    // ── Image bundling ─────────────────────────────────────────────────

    private async Task AddImagesToArchive(ZipArchive archive, List<Block> blocks)
    {
        var imageIndex = 0;
        foreach (var block in blocks.Where(b => b.Type == "figure"))
        {
            try
            {
                var content = block.Content.RootElement;
                var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(src)) continue;

                var filename = ExtractImageFilename(src, imageIndex);

                // The src may be a storage key or a URL path — extract the storage key
                var storageKey = ExtractStorageKey(src);
                if (string.IsNullOrEmpty(storageKey)) continue;

                var imageStream = await _storageService.DownloadAsync(storageKey);
                var entry = archive.CreateEntry($"figures/{filename}", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                await imageStream.CopyToAsync(entryStream);
                imageIndex++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image for block {BlockId}", block.Id);
            }
        }
    }

    private static string? ExtractStorageKey(string src)
    {
        if (string.IsNullOrEmpty(src)) return null;

        // If it's a full URL, extract the path after /uploads/ or the key portion
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath;
                // Handle /uploads/key pattern (local storage)
                if (path.StartsWith("/uploads/"))
                    return path["/uploads/".Length..];
                // Handle R2/S3 paths — use the path as-is minus leading slash
                return path.TrimStart('/');
            }
            return null;
        }

        // If it starts with /uploads/, strip the prefix
        if (src.StartsWith("/uploads/"))
            return src["/uploads/".Length..];

        // Otherwise treat the whole string as a storage key
        return src;
    }

    // ── README generation ──────────────────────────────────────────────

    private static string GenerateReadme(string title, bool isMultiFile = false, string? layout = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('=', title.Length));
        sb.AppendLine();
        sb.AppendLine("This LaTeX project was generated by Lilia (https://liliaeditor.com)");
        sb.AppendLine();
        sb.AppendLine("## Compilation");
        sb.AppendLine();
        sb.AppendLine("To compile this document, run:");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("pdflatex main.tex");
        sb.AppendLine("bibtex main");
        sb.AppendLine("pdflatex main.tex");
        sb.AppendLine("pdflatex main.tex");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Or use latexmk:");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("latexmk -pdf main.tex");
        sb.AppendLine("```");
        sb.AppendLine();

        if (isMultiFile)
        {
            if (layout == "overleaf")
            {
                sb.AppendLine("## Project Structure (Overleaf-compatible)");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(".");
                sb.AppendLine("├── main.tex              # Main document file");
                sb.AppendLine("├── preamble.tex          # Package imports and settings");
                sb.AppendLine("├── references.bib        # Bibliography database");
                sb.AppendLine("├── frontmatter/");
                sb.AppendLine("│   └── abstract.tex      # Abstract (if present)");
                sb.AppendLine("├── chap1/");
                sb.AppendLine("│   └── chapter.tex       # Chapter 1");
                sb.AppendLine("├── chap2/");
                sb.AppendLine("│   └── chapter.tex       # Chapter 2");
                sb.AppendLine("├── figures/              # Image files");
                sb.AppendLine("│   └── *.png/jpg/pdf");
                sb.AppendLine("└── README.txt            # This file");
                sb.AppendLine("```");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## Project Structure");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(".");
                sb.AppendLine("├── main.tex          # Main document file");
                sb.AppendLine("├── preamble.tex      # Package imports and settings");
                sb.AppendLine("├── references.bib    # Bibliography database");
                sb.AppendLine("├── chapters/         # Chapter files");
                sb.AppendLine("│   └── *.tex");
                sb.AppendLine("├── figures/          # Image files");
                sb.AppendLine("│   └── *.png/jpg/pdf");
                sb.AppendLine("└── README.txt        # This file");
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Requirements");
        sb.AppendLine();
        sb.AppendLine("- TeX distribution (TeX Live, MiKTeX, or MacTeX)");
        sb.AppendLine("- Required packages: amsmath, amssymb, graphicx, hyperref, booktabs");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Generated by Lilia - Professional Academic Writing");

        return sb.ToString();
    }

    // ── BibTeX style mapping ───────────────────────────────────────────

    // FT — natbib citation support. When the document uses any natbib
    // command (\citep/\citet/\citeauthor/\citeyear), the bibliography style
    // must be a natbib-compatible .bst or those commands won't compile;
    // otherwise the plain BibTeX styles are kept so legacy \cite-only docs
    // export exactly as before (no behaviour change).

    private bool DocumentUsesNatbib(List<Block> blocks) =>
        blocks.Any(b =>
        {
            var t = GetContentText(b);
            return !string.IsNullOrEmpty(t) && Lilia.Engines.LaTeXPreamble.NatbibCommandRe.IsMatch(t);
        });

    private static string BibStyleName(string style, bool usesNatbib = false)
    {
        if (usesNatbib)
        {
            // Map to the natbib-compatible variant so \citet/\citep render.
            return style switch
            {
                "unsrt" => "unsrtnat",
                "abbrv" => "abbrvnat",
                "IEEEtran" => "IEEEtran",   // natbib-compatible as-is
                "apalike" => "apalike",     // author-year, natbib-compatible
                // "plain", "alpha"/"alphabetic", and the default fall back to
                // plainnat — natbib's standard author-year style.
                _ => "plainnat"
            };
        }
        return style switch
        {
            "alpha" or "alphabetic" => "alpha",
            "unsrt" => "unsrt",
            "IEEEtran" => "IEEEtran",
            "apalike" => "apalike",
            _ => "plain"
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string GetText(JsonElement content)
    {
        return content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    }

    private static string GetContentText(Block block)
    {
        return GetText(block.Content.RootElement);
    }

    private static string ExtractItemText(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString() ?? "";

        if (item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                return textProp.GetString() ?? "";

            if (item.TryGetProperty("richText", out var richTextProp) && richTextProp.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var span in richTextProp.EnumerateArray())
                {
                    if (span.TryGetProperty("text", out var spanText) && spanText.ValueKind == JsonValueKind.String)
                        sb.Append(spanText.GetString() ?? "");
                }
                return sb.ToString();
            }
        }

        return "";
    }

    // ── biblatex backend ────────────────────────────────────────────────
    // The project is always generated in the natbib form; when the caller asks
    // for the biblatex backend we transform the generated .tex in place. Mode is
    // stored backend-agnostically (as the natbib command), so this is the only
    // place the two backends diverge.

    private static bool IsBiblatex(LaTeXExportOptions options) =>
        string.Equals(options.CitationBackend, "biblatex", StringComparison.OrdinalIgnoreCase);

    // natbib bibliographystyle → biblatex style. Defaults to author-year
    // (citations are typically \citet/\citep, i.e. author-year intent).
    private static string BiblatexStyle(string style) => style switch
    {
        "IEEEtran" => "ieee",
        "alpha" or "alphabetic" => "alphabetic",
        "unsrt" or "numeric" => "numeric",
        // "plain"/"apalike"/default → author-year, mirroring natbib's
        // plain→plainnat upgrade for the author-year citation commands.
        _ => "authoryear",
    };

    /// <summary>Rewrite one generated .tex file from the natbib form to biblatex.</summary>
    private static string ToBiblatex(string tex, string style)
    {
        // Citation commands. citet/citep first (so the bare-\cite pass doesn't
        // touch them); \citeauthor/\citeyear are valid in biblatex, left as-is.
        tex = Regex.Replace(tex, @"\\citet(\*?)\{", "\\textcite$1{");
        tex = Regex.Replace(tex, @"\\citep(\*?)\{", "\\parencite$1{");
        tex = Regex.Replace(tex, @"\\cite\{", "\\autocite{");

        var pkg = $"\\usepackage[backend=biber,style={style}]{{biblatex}}\n\\addbibresource{{references.bib}}";
        if (Regex.IsMatch(tex, @"\\usepackage\{natbib\}"))
            // Replace natbib (and its preceding comment line) with biblatex.
            tex = Regex.Replace(tex, @"(?:% natbib[^\n]*\n)?\\usepackage\{natbib\}", pkg);
        else if (Regex.IsMatch(tex, @"\\bibliography\{references\}") && !tex.Contains("{biblatex}"))
            // No natbib was loaded (e.g. bare \cite only) — inject biblatex after \documentclass.
            tex = Regex.Replace(tex, @"(\\documentclass[^\n]*\n)", "$1" + pkg + "\n");

        // Bibliography emission: drop \bibliographystyle, swap \bibliography{references}.
        tex = Regex.Replace(tex, @"\\bibliographystyle\{[^}]*\}\s*\n?", "");
        tex = Regex.Replace(tex, @"\\bibliography\{references\}", "\\printbibliography");
        return tex;
    }

    /// <summary>Apply the biblatex transform to every .tex file in the project.</summary>
    private static List<ProjectFile> ApplyCitationBackend(List<ProjectFile> files, LaTeXExportOptions options)
    {
        if (!IsBiblatex(options)) return files;
        var style = BiblatexStyle(options.BibliographyStyle);
        for (int i = 0; i < files.Count; i++)
            if (files[i].Path.EndsWith(".tex"))
                files[i] = files[i] with { Content = ToBiblatex(files[i].Content, style) };
        return files;
    }

    private record ProjectFile(string Path, string Content);
}
