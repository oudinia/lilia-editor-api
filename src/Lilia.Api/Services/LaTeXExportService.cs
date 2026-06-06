using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        var files = ApplyCitationBackend(GenerateSingleFile(doc, blocks, bibEntries, options), options);
        return files.First(f => f.Path == "main.tex").Content;
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
                .Where(f => f.Path.EndsWith(".tex") || f.Path.EndsWith(".bib") || f.Path.EndsWith(".bst"))
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
        var files = options.Structure switch
        {
            "multi" when options.MultiFileLayout == "overleaf" =>
                GenerateMultiFileOverleaf(doc, blocks, bibEntries, options),
            "multi" =>
                GenerateMultiFileFlat(doc, blocks, bibEntries, options),
            _ =>
                GenerateSingleFile(doc, blocks, bibEntries, options)
        };
        return ApplyCitationBackend(files, options);
    }

    // ── Single-file structure ──────────────────────────────────────────

    private List<ProjectFile> GenerateSingleFile(
        Document doc,
        List<Block> blocks,
        List<BibliographyEntry> bibEntries,
        LaTeXExportOptions options)
    {
        var files = new List<ProjectFile>();
        var sb = new StringBuilder();
        var usesNatbib = DocumentUsesNatbib(blocks);

        // Preamble embedded in main.tex
        sb.AppendLine(BuildDocumentClassDirective(doc, options));
        sb.AppendLine();
        sb.Append(GeneratePackageLines(doc, options, usesNatbib));
        sb.AppendLine();

        // Document info
        sb.AppendLine("% Document info");
        sb.AppendLine($@"\title{{{EscapeLatex(doc.Title)}}}");
        sb.AppendLine(@"\author{}");
        sb.AppendLine(@"\date{\today}");
        sb.AppendLine();

        // Begin document
        sb.AppendLine(@"\begin{document}");
        sb.AppendLine();
        sb.AppendLine(@"\maketitle");
        sb.AppendLine();

        // Abstract (if exists)
        var abstractBlock = blocks.FirstOrDefault(b => b.Type == "abstract");
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
        var mainBlocks = blocks.Where(b => b.Type != "abstract" && b.Type != "bibliography").ToList();
        mainBlocks = StripDuplicateTitleHeading(doc.Title, mainBlocks);

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
        main.AppendLine($@"\title{{{EscapeLatex(doc.Title)}}}");
        main.AppendLine(@"\author{}");
        main.AppendLine(@"\date{\today}");
        main.AppendLine();
        main.AppendLine(@"\begin{document}");
        main.AppendLine();
        main.AppendLine(@"\maketitle");
        main.AppendLine();

        // Abstract
        var abstractBlock = blocks.FirstOrDefault(b => b.Type == "abstract");
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
        files.Add(new ProjectFile("preamble.tex", GeneratePreambleFile(doc, options, usesNatbib)));

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
        var files = new List<ProjectFile>();
        var chapters = GroupBlocksByHeading(blocks);
        var chapterInputPaths = new List<string>();

        // Abstract as frontmatter
        var abstractBlock = blocks.FirstOrDefault(b => b.Type == "abstract");
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
        main.AppendLine($@"\title{{{EscapeLatex(doc.Title)}}}");
        main.AppendLine(@"\author{}");
        main.AppendLine(@"\date{\today}");
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
        files.Add(new ProjectFile("preamble.tex", GeneratePreambleFile(doc, options, usesNatbib)));

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
    private static readonly HashSet<string> DefaultPreamblePackages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Loaded by our default preamble
        "inputenc", "fontenc", "textcomp", "lmodern",
        "amsmath", "amssymb", "amsfonts", "amsthm", "mathtools", "mathrsfs", "cancel", "siunitx",
        "microtype", "setspace", "parskip",
        "graphicx", "float", "caption", "subcaption", "xcolor",
        "booktabs", "multirow", "tabularx", "longtable", "array",
        "enumitem", "listings",
        "algorithm", "algorithmic",
        "tcolorbox", "hyperref", "cleveref", "csquotes",
        "geometry", "babel",
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
    };

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

    private string GeneratePreambleFile(Document doc, LaTeXExportOptions options, bool usesNatbib = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("% Preamble file - included by main.tex");
        sb.AppendLine();
        sb.Append(GeneratePackageLines(doc, options, usesNatbib));
        return sb.ToString();
    }

    private string GeneratePackageLines(Document doc, LaTeXExportOptions options, bool usesNatbib = false)
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
        sb.Append(LaTeXPreamble.Packages);
        sb.AppendLine();

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
        sb.Append(LaTeXPreamble.TheoremEnvironments);

        // Optional domain-specific packages
        if (options.IncludePhysics)
        {
            sb.AppendLine();
            sb.AppendLine("% Physics notation");
            sb.AppendLine(@"\usepackage{physics}");
        }

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

        // User-authored custom preamble (macros / environments) — emitted last,
        // after all packages + layout, so the author's \newcommand /
        // \newenvironment / \DeclareMathOperator can build on everything loaded
        // above and override defaults. Verbatim; the author owns its validity.
        if (!string.IsNullOrWhiteSpace(doc.CustomPreamble))
        {
            sb.AppendLine();
            sb.AppendLine("% Custom preamble (author-defined macros)");
            sb.AppendLine(doc.CustomPreamble.Trim());
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
        var parts = new List<string>();
        foreach (var block in blocks)
        {
            var rendered = RenderBlock(block);
            if (!string.IsNullOrEmpty(rendered))
                parts.Add(rendered);
        }
        return string.Join("\n\n", parts);
    }

    private string RenderBlock(Block block)
    {
        try
        {
            var content = block.Content.RootElement;

            return block.Type switch
            {
                "paragraph" => RenderParagraph(content),
                "heading" => RenderHeading(content),
                "equation" => RenderEquation(content),
                "figure" => RenderFigure(content),
                "table" => RenderTable(content),
                "code" => RenderCode(content),
                "list" => RenderList(content),
                "blockquote" => RenderBlockquote(content),
                "theorem" => RenderTheorem(content),
                "algorithm" or "algorithm2e" => RenderAlgorithm(content),
                // Match both canonical camelCase (BlockTypes constants)
                // and the all-lowercase forms legacy / imported data uses.
                "tableOfContents" or "tableofcontents" or "toc" => @"\tableofcontents" + "\n" + @"\newpage",
                "pageBreak" or "pagebreak" or "page_break" => @"\newpage",
                "columnBreak" or "columnbreak" or "column_break" => @"\columnbreak",
                "columnLayout" => RenderColumnLayout(content),
                "personalInfo" => RenderPersonalInfo(content),
                "photo" => RenderPhoto(content),
                "cvEntry" => RenderCvEntry(content),
                "cvSection" => RenderCvSection(content),
                "embed" => RenderEmbed(content),
                "callout" => RenderCallout(content),
                "abstract" => "", // handled separately
                "bibliography" => "", // handled via .bib file
                _ => RenderUnknownBlock(block),
            };
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

    private string RenderParagraph(JsonElement content)
    {
        var text = GetText(content);
        return FormatInlineContent(text);
    }

    private static string RenderColumnLayout(JsonElement content)
    {
        // Paired marker — mode="start" opens a multicols region, mode="end" closes it.
        var mode = content.TryGetProperty("mode", out var m) ? m.GetString() ?? "start" : "start";
        if (string.Equals(mode, "end", StringComparison.OrdinalIgnoreCase))
            return @"\end{multicols}";
        var columns = content.TryGetProperty("columns", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 2;
        columns = Math.Clamp(columns, 1, 3);
        return columns >= 2
            ? $@"\begin{{multicols}}{{{columns}}}"
            : @"\onecolumn";
    }

    private string RenderPersonalInfo(JsonElement content)
    {
        var name = GetString(content, "name");
        var headline = GetString(content, "headline");
        var email = GetString(content, "email");
        var homepage = GetString(content, "homepage");
        var extra = GetString(content, "extra");

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(name))
            sb.AppendLine(@$"\noindent\textbf{{\LARGE {EscapeLatex(name)}}}\par\smallskip");
        if (!string.IsNullOrEmpty(headline))
            sb.AppendLine($@"\textit{{{EscapeLatex(headline)}}}\par");
        var line = new List<string>();
        if (!string.IsNullOrEmpty(email)) line.Add(@$"\texttt{{{EscapeLatex(email)}}}");
        if (content.TryGetProperty("phones", out var phonesEl) && phonesEl.ValueKind == JsonValueKind.Array)
            foreach (var p in phonesEl.EnumerateArray())
            {
                var num = p.TryGetProperty("number", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(num)) line.Add(EscapeLatex(num));
            }
        if (!string.IsNullOrEmpty(homepage)) line.Add($@"\url{{{homepage}}}");
        if (line.Count > 0) sb.AppendLine(string.Join(@" \ $\cdot$\ ", line) + @"\par");
        if (content.TryGetProperty("socials", out var socialsEl) && socialsEl.ValueKind == JsonValueKind.Array)
            foreach (var s in socialsEl.EnumerateArray())
            {
                var network = s.TryGetProperty("network", out var nEl) ? nEl.GetString() : "";
                var handle = s.TryGetProperty("handle", out var hEl) ? hEl.GetString() : "";
                if (!string.IsNullOrEmpty(handle))
                    sb.AppendLine(@$"\small\textbf{{{EscapeLatex(network ?? "")}:}} {EscapeLatex(handle)}\par");
            }
        if (!string.IsNullOrEmpty(extra))
            sb.AppendLine($@"\smallskip\textit{{{EscapeLatex(extra)}}}\par");
        return sb.ToString().TrimEnd();
    }

    private string RenderPhoto(JsonElement content)
    {
        var src = GetString(content, "src");
        if (string.IsNullOrEmpty(src)) return "";
        var size = content.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt32() : 64;
        var position = GetString(content, "position"); // left|right|center
        var align = position switch
        {
            "left" => "flushleft",
            "right" => "flushright",
            _ => "center"
        };
        var filename = ExtractImageFilename(src);
        return $@"\begin{{{align}}}
\IfFileExists{{figures/{filename}}}{{\includegraphics[height={size}pt]{{figures/{filename}}}}}{{\fbox{{\parbox{{{size}pt}}{{\centering\small [photo]}}}}}}
\end{{{align}}}";
    }

    private string RenderCvSection(JsonElement content)
    {
        var title = GetString(content, "title");
        return @$"\par\medskip\noindent\textbf{{\Large {EscapeLatex(title)}}}\par\smallskip";
    }

    private string RenderCvEntry(JsonElement content)
    {
        var period = GetString(content, "period");
        var role = GetString(content, "role");
        var org = GetString(content, "org");
        var location = GetString(content, "location");
        var description = GetString(content, "description");
        var sb = new StringBuilder();
        sb.Append(@$"\noindent\textbf{{{EscapeLatex(role)}}}");
        if (!string.IsNullOrEmpty(org)) sb.Append(@$", \emph{{{EscapeLatex(org)}}}");
        if (!string.IsNullOrEmpty(location)) sb.Append(@$" — {EscapeLatex(location)}");
        if (!string.IsNullOrEmpty(period)) sb.Append(@$" \hfill {EscapeLatex(period)}");
        sb.AppendLine(@"\par");
        if (!string.IsNullOrEmpty(description))
            sb.AppendLine(FormatInlineContent(description) + @"\par");
        return sb.ToString().TrimEnd();
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private string RenderHeading(JsonElement content)
    {
        var text = GetText(content);
        var level = content.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
        var commands = new[] { "section", "subsection", "subsubsection", "paragraph", "subparagraph" };
        var command = commands[Math.Min(level - 1, 4)];
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{{label}}}" : "";

        // Starred form: `numbered: false` emits \section*{} which
        // suppresses both the auto-number and the TOC entry. Matches
        // LaTeX's behavior — see "Designing Pages" §sectioning.
        var numbered = !content.TryGetProperty("numbered", out var nEl)
            || nEl.ValueKind != JsonValueKind.False;
        var star = numbered ? "" : "*";

        // Optional short title for the TOC entry — \section[short]{full}.
        // Only meaningful when numbered (TOC entry exists); silently
        // drop the bracketed arg on starred headings.
        var shortTitle = content.TryGetProperty("shortTitle", out var stEl) && stEl.ValueKind == JsonValueKind.String
            ? stEl.GetString() ?? ""
            : "";
        var shortPart = numbered && !string.IsNullOrEmpty(shortTitle)
            ? $"[{FormatInlineContent(shortTitle)}]"
            : "";

        // Route through FormatInlineContent so `**Important**` becomes
        // `\textbf{Important}` (parity with paragraph + preview render).
        // Strip baked-in numbering prefix ("1. ", "1.1 ", "I. ", "A. ")
        // mirroring SectionKeywordRegistry.StripNumberingPrefix on the
        // import path. Without this, LaTeX auto-numbering shows the
        // number twice in the rendered PDF and the TOC.
        var stripped = StripBakedNumberingPrefixForHeading(text);
        return $@"\{command}{star}{shortPart}{{{FormatInlineContent(stripped)}}}{labelPart}";
    }

    /// <summary>
    /// Mirror of <c>SectionKeywordRegistry.StripNumberingPrefix</c>.
    /// </summary>
    private static string StripBakedNumberingPrefixForHeading(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var match = System.Text.RegularExpressions.Regex.Match(text,
            @"^(?:\d+(?:\.\d+)*\.?\s+|[IVXLC]+\.\s+|[A-Z]\.\s+)(.+)$");
        return match.Success ? match.Groups[1].Value : text;
    }

    private string RenderEquation(JsonElement content)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";
        var mode = content.TryGetProperty("mode", out var m) ? m.GetString() ?? "display" : "display";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var numbered = !content.TryGetProperty("numbered", out var n) || n.ValueKind != JsonValueKind.False;
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{eq:{label}}}" : "";

        if (mode == "inline")
            return $"${latex}$";
        if (mode == "align")
            return $@"\begin{{align{(numbered ? "" : "*")}}}{labelPart}" + "\n" + latex + "\n" + $@"\end{{align{(numbered ? "" : "*")}}}";
        if (mode == "gather")
            return $@"\begin{{gather{(numbered ? "" : "*")}}}{labelPart}" + "\n" + latex + "\n" + $@"\end{{gather{(numbered ? "" : "*")}}}";
        if (numbered)
            return $@"\begin{{equation}}{labelPart}" + "\n" + latex + "\n" + @"\end{equation}";
        return $@"\[" + "\n" + latex + "\n" + @"\]";
    }

    private string RenderFigure(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
        // span="page" → \begin{figure*}, which in single-column mode behaves
        // identically to \begin{figure}, so we can emit it unconditionally.
        var span = content.TryGetProperty("span", out var sp) ? sp.GetString() ?? "column" : "column";
        var env = string.Equals(span, "page", StringComparison.OrdinalIgnoreCase) ? "figure*" : "figure";

        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{fig:{label}}}" : "";

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
                    fsb.AppendLine($@"\caption{{{EscapeLatex(sfCap)}}}" + (!string.IsNullOrEmpty(sfLabel) ? $@"\label{{fig:{sfLabel}}}" : ""));
                fsb.AppendLine(@"\end{subfigure}");
                // a little horizontal gap between panels (not after the last)
                if (i < panels.Count - 1) fsb.AppendLine(@"\hfill");
            }
            if (!string.IsNullOrEmpty(caption))
                fsb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}{labelPart}");
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
            sb.AppendLine($@"  \includegraphics[width=0.8\textwidth]{{figures/{filename}}}%");
            sb.AppendLine($@"}}{{\fbox{{\parbox{{0.7\textwidth}}{{\centering\small\textit{{[Missing figure: {EscapeLatex(filename)}]}}}}}}}}");
        }
        else
        {
            sb.AppendLine(@"% [figure placeholder — no image uploaded]");
        }
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}{labelPart}");
        else if (!string.IsNullOrEmpty(labelPart))
            sb.AppendLine(labelPart);
        sb.Append($@"\end{{{env}}}");
        return sb.ToString();
    }

    /// <summary>
    /// Build the LaTeX column specifier from the table block's
    /// columnAlign + columnWidth fields. Each entry in columnAlign
    /// is one of "l" (left), "c" (center), "r" (right), or "p" (paragraph
    /// — needs a matching width). columnWidth is parallel: same index
    /// gives the cell width for "p"/"m"/"b" columns. Falls back to "l"
    /// for any column not covered by either array, so partial schemas
    /// degrade safely.
    /// </summary>
    private static string BuildColumnSpec(JsonElement content, int colCount)
    {
        string[] aligns = Array.Empty<string>();
        if (content.TryGetProperty("columnAlign", out var alignEl) && alignEl.ValueKind == JsonValueKind.Array)
        {
            aligns = alignEl.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "l" : "l")
                .ToArray();
        }
        string[] widths = Array.Empty<string>();
        if (content.TryGetProperty("columnWidth", out var widEl) && widEl.ValueKind == JsonValueKind.Array)
        {
            widths = widEl.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "")
                .ToArray();
        }

        var sb = new StringBuilder();
        for (int i = 0; i < colCount; i++)
        {
            var raw = (i < aligns.Length ? aligns[i] : "l").Trim().ToLowerInvariant();
            // Normalise: anything other than the supported set falls back
            // to 'l'. Prevents user-typed garbage breaking pdflatex.
            switch (raw)
            {
                case "c":
                case "r":
                    sb.Append(raw);
                    break;
                case "p":
                case "m":
                case "b":
                    var w = i < widths.Length ? widths[i] : "";
                    sb.Append(string.IsNullOrWhiteSpace(w) ? "l" : $"{raw}{{{w}}}");
                    break;
                default:
                    sb.Append('l');
                    break;
            }
        }
        return sb.ToString();
    }

    private string RenderTable(JsonElement content)
    {
        var hasHeaders = content.TryGetProperty("headers", out var headers)
            && headers.ValueKind == JsonValueKind.Array
            && headers.GetArrayLength() > 0;

        if (!content.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return "";

        var rowList = rows.EnumerateArray().ToList();
        var colCount = hasHeaders
            ? headers.GetArrayLength()
            : rowList.Count > 0 && rowList[0].ValueKind == JsonValueKind.Array
                ? rowList[0].GetArrayLength()
                : 1;
        // Per-column alignment: read columnAlign + optional columnWidth
        // from the block content. Pre-fix this rendered every column as
        // 'l' regardless of what the schema said.
        var colSpec = BuildColumnSpec(content, colCount);

        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var shortCaption = content.TryGetProperty("shortCaption", out var sc) && sc.ValueKind == JsonValueKind.String
            ? sc.GetString() ?? ""
            : "";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{tbl:{label}}}" : "";
        var span = content.TryGetProperty("span", out var sp) ? sp.GetString() ?? "column" : "column";
        var env = string.Equals(span, "page", StringComparison.OrdinalIgnoreCase) ? "table*" : "table";

        // longtable: page-breaking tables. When set, swap tabular for the
        // longtable environment (already in the preamble) and repeat the
        // header on each page. longtable IS its own float, so we do NOT wrap it
        // in table/table* — its \caption goes inside the environment.
        var longTable = (content.TryGetProperty("longTable", out var ltA) && ltA.ValueKind == JsonValueKind.True)
            || (content.TryGetProperty("longtable", out var ltB) && ltB.ValueKind == JsonValueKind.True);

        var captionLine = !string.IsNullOrEmpty(caption)
            ? (!string.IsNullOrEmpty(shortCaption)
                ? $@"\caption[{EscapeLatex(shortCaption)}]{{{EscapeLatex(caption)}}}{labelPart}"
                : $@"\caption{{{EscapeLatex(caption)}}}{labelPart}")
            : "";

        var sb = new StringBuilder();
        if (!longTable)
        {
            sb.AppendLine($@"\begin{{{env}}}[H]");
            sb.AppendLine(@"\centering");
            if (!string.IsNullOrEmpty(captionLine)) sb.AppendLine(captionLine);
        }

        // Per-column alignment letters for \multicolumn (the leftmost column of
        // a span drives its alignment).
        var colAligns = ResolveColumnAligns(content, colCount);

        // Build the coverage maps from span origins so the export can emit
        // \multicolumn / \multirow and skip the cells a span absorbs. The
        // editor keeps full-width rows (data index == grid column), so a span
        // origin is an object {content,colspan,rowspan} and the cells it covers
        // remain in the grid but are dropped here.
        var origins = new Dictionary<(int, int), (int Cs, int Rs, string Text)>();
        var contLeft = new Dictionary<(int, int), int>(); // rowspan continuation: (r,c0) -> colspan width
        var covered = new HashSet<(int, int)>();
        for (int r = 0; r < rowList.Count; r++)
        {
            if (rowList[r].ValueKind != JsonValueKind.Array) continue;
            var cells = rowList[r].EnumerateArray().ToList();
            for (int c = 0; c < cells.Count; c++)
            {
                var (cs, rs) = TableCellSpan(cells[c]);
                if (cs <= 1 && rs <= 1) continue;
                origins[(r, c)] = (cs, rs, TableCellText(cells[c]));
                for (int rr = r; rr < r + rs; rr++)
                    for (int cc = c; cc < c + cs; cc++)
                    {
                        if (rr == r && cc == c) continue;
                        covered.Add((rr, cc));
                        if (rr > r && cc == c) contLeft[(rr, cc)] = cs; // left edge of a continuation row
                    }
            }
        }

        // Emit a single body row (grid-aware).
        string EmitRow(int r, List<JsonElement> cells)
        {
            var toks = new List<string>();
            int c = 0;
            while (c < colCount)
            {
                var align = c < colAligns.Length ? colAligns[c] : "l";
                if (origins.TryGetValue((r, c), out var o))
                {
                    var inner = EscapeLatex(o.Text);
                    if (o.Rs > 1) inner = $@"\multirow{{{o.Rs}}}{{*}}{{{inner}}}";
                    toks.Add(o.Cs > 1 ? $@"\multicolumn{{{o.Cs}}}{{{align}}}{{{inner}}}" : inner);
                    c += Math.Max(1, o.Cs);
                }
                else if (contLeft.TryGetValue((r, c), out var cw))
                {
                    // Continuation row of a rowspan: leave the slot blank but
                    // keep the column count consistent.
                    toks.Add(cw > 1 ? $@"\multicolumn{{{cw}}}{{{align}}}{{}}" : "");
                    c += Math.Max(1, cw);
                }
                else if (covered.Contains((r, c)))
                {
                    c += 1; // absorbed by a span; nothing to emit
                }
                else
                {
                    var text = c < cells.Count ? TableCellText(cells[c]) : "";
                    toks.Add(EscapeLatex(text));
                    c += 1;
                }
            }
            return string.Join(" & ", toks) + @" \\";
        }

        var tableEnv = longTable ? "longtable" : "tabular";
        if (longTable)
        {
            sb.AppendLine($@"\begin{{longtable}}{{{colSpec}}}");
            // longtable's \caption lives inside the env and ends the line.
            if (!string.IsNullOrEmpty(captionLine)) sb.AppendLine(captionLine + @" \\");
        }
        else sb.AppendLine($@"\begin{{tabular}}{{{colSpec}}}");
        sb.AppendLine(@"\toprule");

        if (hasHeaders)
        {
            var headerCells = headers.EnumerateArray()
                .Select(h => $@"\textbf{{{EscapeLatex(TableCellText(h))}}}")
                .ToList();
            sb.AppendLine(string.Join(" & ", headerCells) + @" \\");
            sb.AppendLine(@"\midrule");
            // longtable: repeat the header on every page, then mark the body.
            if (longTable) { sb.AppendLine(@"\endfirsthead"); sb.AppendLine(string.Join(" & ", headerCells) + @" \\"); sb.AppendLine(@"\midrule"); sb.AppendLine(@"\endhead"); }
        }

        for (int r = 0; r < rowList.Count; r++)
            if (rowList[r].ValueKind == JsonValueKind.Array)
                sb.AppendLine(EmitRow(r, rowList[r].EnumerateArray().ToList()));

        sb.AppendLine(@"\bottomrule");
        sb.AppendLine($@"\end{{{tableEnv}}}");
        if (longTable) return sb.ToString().TrimEnd();
        sb.Append($@"\end{{{env}}}");
        return sb.ToString();
    }

    // A table cell is either a plain string or an object {content|text, colspan, rowspan}.
    private static string TableCellText(JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.String) return cell.GetString() ?? "";
        if (cell.ValueKind == JsonValueKind.Object)
        {
            if (cell.TryGetProperty("content", out var ct) && ct.ValueKind == JsonValueKind.String) return ct.GetString() ?? "";
            if (cell.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String) return tx.GetString() ?? "";
        }
        return "";
    }

    private static (int Colspan, int Rowspan) TableCellSpan(JsonElement cell)
    {
        if (cell.ValueKind != JsonValueKind.Object) return (1, 1);
        int cs = cell.TryGetProperty("colspan", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 1;
        int rs = cell.TryGetProperty("rowspan", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 1;
        return (Math.Max(1, cs), Math.Max(1, rs));
    }

    // Per-column alignment letters (c/l/r), reading either `alignments` (editor)
    // or `columnAlign` (legacy). Used for \multicolumn; defaults to 'l'.
    private static string[] ResolveColumnAligns(JsonElement content, int colCount)
    {
        JsonElement arr = default;
        var has = (content.TryGetProperty("alignments", out arr) && arr.ValueKind == JsonValueKind.Array)
            || (content.TryGetProperty("columnAlign", out arr) && arr.ValueKind == JsonValueKind.Array);
        var result = new string[colCount];
        for (int i = 0; i < colCount; i++)
        {
            var raw = has && i < arr.GetArrayLength()
                ? (arr[i].ValueKind == JsonValueKind.String ? arr[i].GetString() ?? "l" : "l")
                : "l";
            raw = raw.Trim().ToLowerInvariant();
            result[i] = raw switch { "center" or "c" => "c", "right" or "r" => "r", _ => "l" };
        }
        return result;
    }

    // Languages the `listings` package supports out of the box (canonical
    // casing — listings is case-sensitive). Any value outside this set
    // (including "text", "plaintext", "plain", empty, or an exotic language
    // name from DOCX import) causes pdflatex to fail with "Package Listings
    // Error: Couldn't load requested language." Unknown languages are mapped
    // to a bare `\begin{lstlisting}` with no language option — listings
    // renders that as plain code with no syntax highlighting.
    private static readonly HashSet<string> ListingsLanguages = new(StringComparer.Ordinal)
    {
        "Ada", "Assembler", "Awk", "bash", "C", "C++", "Caml", "Clean", "Cobol",
        "Csh", "CSS", "Delphi", "Eiffel", "Erlang", "Euphoria", "Fortran",
        "GCL", "Gnuplot", "Haskell", "HTML", "IDL", "inform", "Java", "JVMIS",
        "ksh", "Lisp", "Logo", "Lua", "make", "Mathematica", "Matlab",
        "Mercury", "MetaPost", "Miranda", "Mizar", "ML", "Modula-2", "MuPAD",
        "NASTRAN", "Oberon-2", "OCL", "Octave", "Oz", "Pascal", "Perl", "PHP",
        "PL/I", "Plasm", "PostScript", "POV", "Prolog", "Promela", "PSTricks",
        "Python", "R", "Reduce", "Rexx", "RSL", "Ruby", "S", "SAS", "Scilab",
        "sh", "SHELXL", "Simula", "SQL", "tcl", "TeX", "VBScript", "Verilog",
        "VHDL", "VRML", "XML", "XSLT",
    };

    // Some languages aren't native to listings but have a close enough match
    // we can safely substitute. Values must be canonical listings names or "".
    private static readonly Dictionary<string, string> LanguageAliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "JavaScript",      // JavaScript not native but commonly added
        ["javascript"] = "JavaScript",
        ["ts"] = "JavaScript",      // listings has no TS; JS highlighting is close enough
        ["typescript"] = "JavaScript",
        ["sh"] = "bash",
        ["shell"] = "bash",
        ["zsh"] = "bash",
        ["py"] = "Python",
        ["cpp"] = "C++",
        ["c#"] = "Java",            // closest listings fit
        ["csharp"] = "Java",
        ["latex"] = "TeX",
        ["tex"] = "TeX",
        // Languages listings doesn't ship with — fall back to plain (no highlighting).
        ["json"] = "",
        ["yaml"] = "",
        ["yml"] = "",
        ["toml"] = "",
        ["go"] = "",
        ["golang"] = "",
        ["rust"] = "",
        ["kotlin"] = "Java",
        ["swift"] = "",
        ["solidity"] = "",
        ["dart"] = "",
        ["zig"] = "",
        ["elixir"] = "",
        ["markdown"] = "",
        ["md"] = "",
        ["text"] = "",
        ["plaintext"] = "",
        ["plain"] = "",
        ["txt"] = "",
    };

    // Returns a listings-valid language name, or "" when the input has no safe mapping.
    // Case-insensitive lookup; always returns the canonical listings casing.
    private static string NormalizeListingsLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var trimmed = raw.Trim();
        if (LanguageAliasMap.TryGetValue(trimmed, out var mapped))
            return mapped;
        // Case-insensitive whitelist match — return the canonical (dictionary) form
        // so listings gets the exact string it expects.
        foreach (var known in ListingsLanguages)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                return known;
        }
        return "";
    }

    private string RenderCode(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var rawLanguage = content.TryGetProperty("language", out var l) ? l.GetString() : null;
        var language = NormalizeListingsLanguage(rawLanguage);
        var opening = string.IsNullOrEmpty(language)
            ? @"\begin{lstlisting}"
            : $@"\begin{{lstlisting}}[language={language}]";
        return opening + "\n" + code + "\n" + @"\end{lstlisting}";
    }

    private string RenderList(JsonElement content)
    {
        var isOrdered = false;
        if (content.TryGetProperty("listType", out var lt))
            isOrdered = lt.GetString() == "ordered";
        else if (content.TryGetProperty("ordered", out var ord))
            isOrdered = ord.ValueKind == JsonValueKind.True;

        // `kind` takes precedence over `ordered` when present (Phase 2).
        string? kind = null;
        if (content.TryGetProperty("kind", out var kindProp) && kindProp.ValueKind == JsonValueKind.String)
            kind = kindProp.GetString();
        var isDescription = kind == "description";

        var env = isDescription ? "description" : (isOrdered ? "enumerate" : "itemize");

        // enumitem options — mirror RenderService.RenderListToLatex so
        // /preview/latex and /export/pdf agree. `spacing` applies to
        // all three envs; labelFormat/start are ordered-only.
        var enumOptions = new List<string>();
        if (content.TryGetProperty("spacing", out var spProp) && spProp.ValueKind == JsonValueKind.String)
        {
            var spacingOption = spProp.GetString() switch
            {
                "tight" => "noitemsep",
                "compact" => "nosep",
                _ => null,
            };
            if (spacingOption != null) enumOptions.Add(spacingOption);
        }
        if (isOrdered && !isDescription)
        {
            if (content.TryGetProperty("labelFormat", out var lfProp))
            {
                var labelOption = lfProp.GetString() switch
                {
                    "alpha" => @"label=(\alph*)",
                    "Alpha" => @"label=(\Alph*)",
                    "roman" => @"label=(\roman*)",
                    "Roman" => @"label=(\Roman*)",
                    _ => null,
                };
                if (labelOption != null) enumOptions.Add(labelOption);
            }
            if (content.TryGetProperty("start", out var startProp)
                && startProp.TryGetInt32(out var startNum) && startNum != 1)
            {
                enumOptions.Add($"start={startNum}");
            }
        }

        var sb = new StringBuilder();
        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            AppendListItems(items, env, depth: 0, sb, enumOptions);
        }
        else
        {
            sb.AppendLine($@"\begin{{{env}}}");
            sb.Append($@"\end{{{env}}}");
        }
        return sb.ToString().TrimEnd();
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

    private string RenderCallout(JsonElement content)
    {
        // Mirror of RenderService.RenderCalloutToLatex — keep the two
        // paths aligned so /preview/latex and /export/pdf emit the same
        // tcolorbox for the same content. Free-form color override:
        // when `color` is set, paint colback/colframe with that xcolor
        // name; otherwise use the tcolorbox theme default.
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = GetText(content);
        var color = content.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "";
        var displayTitle = !string.IsNullOrEmpty(title) ? title : char.ToUpper(variant[0]) + variant[1..];
        var titlePart = $"title={{{EscapeLatex(displayTitle)}}}";
        var colorPart = !string.IsNullOrEmpty(color)
            ? $", colback={color}!10!white, colframe={color}!75!black, coltitle=white"
            : "";
        return $@"\begin{{tcolorbox}}[{titlePart}{colorPart}]" + "\n" + FormatInlineContent(text) + "\n" + @"\end{tcolorbox}";
    }

    private string RenderBlockquote(JsonElement content)
    {
        var text = GetText(content);
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "simple" : "simple";
        var attribution = content.TryGetProperty("attribution", out var a) ? a.GetString() ?? "" : "";

        // Mirror of RenderService.RenderBlockquoteToLatex variant routing
        // — keep the two paths aligned so /preview/latex + /export/pdf
        // emit identical LaTeX for the same content.
        switch (variant)
        {
            case "epigraph":
                return $@"\epigraph{{{FormatInlineContent(text)}}}{{{FormatInlineContent(attribution)}}}";
            case "verse":
            {
                var lines = (text ?? "").Split('\n');
                var body = string.Join(" \\\\\n", lines.Select(l => FormatInlineContent(l)));
                return $@"\begin{{verse}}" + "\n" + body + "\n" + @"\end{verse}";
            }
            default:
                return $@"\begin{{quote}}" + "\n" + FormatInlineContent(text) + "\n" + @"\end{quote}";
        }
    }

    /// <summary>
    /// Algorithm block → an <c>algorithm</c> float wrapping <c>algorithmic</c>
    /// pseudocode (both packages are in the shared preamble). Body is either a
    /// <c>lines</c> array or a freeform <c>body</c>/<c>code</c> string (split on
    /// newlines); each line becomes a numbered \STATE. Lines pass through
    /// FormatInlineContent so inline math ($…$) and emphasis survive.
    /// </summary>
    private string RenderAlgorithm(JsonElement content)
    {
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{alg:{label}}}" : "";

        // Build the algorithmic body. The editor stores `lines` as structured
        // objects { indent, keyword, text, comment }; legacy/imported data may
        // store `lines` as plain strings or a freeform `body`/`code` string.
        // Each line becomes a numbered \STATE that always compiles (keywords
        // are bold text + indent via \hspace, rather than \IF/\ENDIF pairs the
        // flat line model can't guarantee are balanced).
        var rendered = new List<string>();
        if (content.TryGetProperty("lines", out var ln) && ln.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in ln.EnumerateArray())
            {
                if (line.ValueKind == JsonValueKind.String)
                {
                    var t = (line.GetString() ?? "").Trim();
                    if (t.Length > 0) rendered.Add($@"\STATE {FormatInlineContent(t)}");
                }
                else if (line.ValueKind == JsonValueKind.Object)
                {
                    var indent = line.TryGetProperty("indent", out var iv) && iv.ValueKind == JsonValueKind.Number ? iv.GetInt32() : 0;
                    var keyword = line.TryGetProperty("keyword", out var kv) ? kv.GetString() ?? "" : "";
                    var text = line.TryGetProperty("text", out var txv) ? txv.GetString() ?? "" : "";
                    var comment = line.TryGetProperty("comment", out var cv) ? cv.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(keyword) && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(comment))
                        continue;
                    var p = new StringBuilder(@"\STATE ");
                    if (indent > 0) p.Append($@"\hspace{{{indent}em}}");
                    if (!string.IsNullOrWhiteSpace(keyword)) p.Append($@"\textbf{{{EscapeLatex(keyword)}}} ");
                    if (!string.IsNullOrWhiteSpace(text)) p.Append(FormatInlineContent(text));
                    if (!string.IsNullOrWhiteSpace(comment)) p.Append($@" \COMMENT{{{EscapeLatex(comment)}}}");
                    rendered.Add(p.ToString());
                }
            }
        }
        else
        {
            var body =
                content.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() :
                content.TryGetProperty("code", out var cd) && cd.ValueKind == JsonValueKind.String ? cd.GetString() :
                GetText(content);
            if (!string.IsNullOrEmpty(body))
                foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
                {
                    var t = raw.Trim();
                    if (t.Length > 0) rendered.Add($@"\STATE {FormatInlineContent(t)}");
                }
        }

        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{algorithm}[H]");
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}{labelPart}");
        else if (!string.IsNullOrEmpty(labelPart))
            sb.AppendLine(labelPart);
        sb.AppendLine(@"\begin{algorithmic}[1]");
        foreach (var r in rendered) sb.AppendLine(r);
        sb.AppendLine(@"\end{algorithmic}");
        sb.Append(@"\end{algorithm}");
        return sb.ToString();
    }

    private string RenderTheorem(JsonElement content)
    {
        var theoremType = content.TryGetProperty("theoremType", out var tt) ? tt.GetString() ?? "theorem" : "theorem";
        var text = GetText(content);
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{thm:{label}}}" : "";

        if (theoremType == "proof")
            return $@"\begin{{proof}}" + "\n" + FormatInlineContent(text) + "\n" + @"\end{proof}";

        var titlePart = !string.IsNullOrEmpty(title) ? $"[{EscapeLatex(title)}]" : "";
        return $@"\begin{{{theoremType}}}{titlePart}{labelPart}" + "\n" + FormatInlineContent(text) + "\n" + $@"\end{{{theoremType}}}";
    }

    private string RenderEmbed(JsonElement content)
    {
        var engine = content.TryGetProperty("engine", out var e) ? e.GetString() ?? "" : "";
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";

        if (engine == "latex")
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(caption)) parts.Add($"% {EscapeLatex(caption)}");
            if (!string.IsNullOrEmpty(label)) parts.Add($@"\label{{{label}}}");
            parts.Add(code);
            return string.Join("\n", parts);
        }

        return "% Typst embed (not compiled in LaTeX output)\n% " + code.Replace("\n", "\n% ");
    }

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
        result = Regex.Replace(result, @"\\eqref\{([^}]+)\}", m => Ph($@"\eqref{{{m.Groups[1].Value}}}"));
        result = Regex.Replace(result, @"\\ref\{([^}]+)\}", m => Ph($@"\ref{{{m.Groups[1].Value}}}"));
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
        result = Regex.Replace(result, @"_([^_]+)_", m => Ph($@"\textit{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 5. References: @ref{label} → \ref{label}
        result = Regex.Replace(result, @"@ref\{([^}]+)\}", m => Ph($@"\ref{{{m.Groups[1].Value}}}"));

        // 6. Citations: @cite{key} → \cite{key}
        result = Regex.Replace(result, @"@cite\{([^}]+)\}", m => Ph($@"\cite{{{m.Groups[1].Value}}}"));

        // 7. Escape remaining plain text
        result = EscapeLatex(result);

        // 8. Restore placeholders
        result = Regex.Replace(result, @"\x00PH(\d+)\x00", m => placeholders[int.Parse(m.Groups[1].Value)]);

        return result;
    }

    // ── LaTeX escaping ─────────────────────────────────────────────────

    internal static string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        return text
            .Replace("\\", "\\textbackslash{}")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("$", "\\$")
            .Replace("&", "\\&")
            .Replace("#", "\\#")
            .Replace("^", "\\textasciicircum{}")
            .Replace("_", "\\_")
            .Replace("~", "\\textasciitilde{}")
            .Replace("%", "\\%");
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

            if (block.Type != "abstract" && block.Type != "bibliography")
                currentChapter.Add(block);
        }

        if (currentChapter.Count > 0)
            chapters.Add(currentChapter);

        // If no headings, put all blocks in one chapter
        if (chapters.Count == 0 && blocks.Count > 0)
        {
            chapters.Add(blocks.Where(b => b.Type != "abstract" && b.Type != "bibliography").ToList());
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
    private static readonly System.Text.RegularExpressions.Regex NatbibCommandRe =
        new(@"\\cite(?:p|t|author|year)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private bool DocumentUsesNatbib(List<Block> blocks) =>
        blocks.Any(b =>
        {
            var t = GetContentText(b);
            return !string.IsNullOrEmpty(t) && NatbibCommandRe.IsMatch(t);
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
