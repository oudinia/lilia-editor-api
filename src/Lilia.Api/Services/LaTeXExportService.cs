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

    public LaTeXExportService(
        LiliaDbContext context,
        IStorageService storageService,
        ILogger<LaTeXExportService> logger,
        Lilia.Import.Services.IImportTelemetrySink? telemetry = null)
    {
        _context = context;
        _storageService = storageService;
        _logger = logger;
        _telemetry = telemetry ?? new Lilia.Import.Services.NoopImportTelemetrySink();
    }

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
        var files = GenerateSingleFile(doc, blocks, bibEntries, options);
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
        return options.Structure switch
        {
            "multi" when options.MultiFileLayout == "overleaf" =>
                GenerateMultiFileOverleaf(doc, blocks, bibEntries, options),
            "multi" =>
                GenerateMultiFileFlat(doc, blocks, bibEntries, options),
            _ =>
                GenerateSingleFile(doc, blocks, bibEntries, options)
        };
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

        // Preamble embedded in main.tex
        sb.AppendLine(BuildDocumentClassDirective(doc, options));
        sb.AppendLine();
        sb.Append(GeneratePackageLines(doc, options));
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
            sb.AppendLine($@"\bibliographystyle{{{BibStyleName(options.BibliographyStyle)}}}");
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
            main.AppendLine($@"\bibliographystyle{{{BibStyleName(options.BibliographyStyle)}}}");
            main.AppendLine(@"\bibliography{references}");
        }

        main.AppendLine();
        main.AppendLine(@"\end{document}");

        files.Add(new ProjectFile("main.tex", main.ToString()));
        files.Add(new ProjectFile("preamble.tex", GeneratePreambleFile(doc, options)));

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
            main.AppendLine($@"\bibliographystyle{{{BibStyleName(options.BibliographyStyle)}}}");
            main.AppendLine(@"\bibliography{references}");
        }

        main.AppendLine();
        main.AppendLine(@"\end{document}");

        files.Add(new ProjectFile("main.tex", main.ToString()));
        files.Add(new ProjectFile("preamble.tex", GeneratePreambleFile(doc, options)));

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

    private string GeneratePreambleFile(Document doc, LaTeXExportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("% Preamble file - included by main.tex");
        sb.AppendLine();
        sb.Append(GeneratePackageLines(doc, options));
        return sb.ToString();
    }

    private string GeneratePackageLines(Document doc, LaTeXExportOptions options)
    {
        var sb = new StringBuilder();

        // Imported packages first — user's class often needs them before our defaults.
        var importedPkgs = BuildImportedPackageLines(doc);
        if (!string.IsNullOrEmpty(importedPkgs))
        {
            sb.Append(importedPkgs);
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
        // Route through FormatInlineContent so `**Important**` becomes
        // `\textbf{Important}` (parity with paragraph + preview render).
        // Pre-fix the heading hit EscapeLatex directly which kept the
        // markdown asterisks literal.
        // Strip baked-in numbering prefix ("1. ", "1.1 ", "I. ", "A. ")
        // mirroring SectionKeywordRegistry.StripNumberingPrefix on the
        // import path. Without this, LaTeX auto-numbering shows the
        // number twice ("2  1. Introduction") in the rendered PDF and
        // the TOC, since legacy imports baked the prefix into text.
        var stripped = StripBakedNumberingPrefixForHeading(text);
        return $@"\{command}{{{FormatInlineContent(stripped)}}}{labelPart}";
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
        var colSpec = new string('l', colCount);

        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{tbl:{label}}}" : "";
        var span = content.TryGetProperty("span", out var sp) ? sp.GetString() ?? "column" : "column";
        var env = string.Equals(span, "page", StringComparison.OrdinalIgnoreCase) ? "table*" : "table";

        var sb = new StringBuilder();
        sb.AppendLine($@"\begin{{{env}}}[H]");
        sb.AppendLine(@"\centering");
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}{labelPart}");
        sb.AppendLine($@"\begin{{tabular}}{{{colSpec}}}");
        sb.AppendLine(@"\toprule");

        if (hasHeaders)
        {
            var headerCells = headers.EnumerateArray()
                .Select(h => $@"\textbf{{{EscapeLatex(h.GetString() ?? "")}}}")
                .ToList();
            sb.AppendLine(string.Join(" & ", headerCells) + @" \\");
            sb.AppendLine(@"\midrule");
        }

        foreach (var row in rowList)
        {
            if (row.ValueKind == JsonValueKind.Array)
            {
                var cells = row.EnumerateArray()
                    .Select(c => EscapeLatex(c.GetString() ?? ""))
                    .ToList();
                sb.AppendLine(string.Join(" & ", cells) + @" \\");
            }
        }

        sb.AppendLine(@"\bottomrule");
        sb.AppendLine(@"\end{tabular}");
        sb.Append($@"\end{{{env}}}");
        return sb.ToString();
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

        var env = isOrdered ? "enumerate" : "itemize";
        var sb = new StringBuilder();
        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            AppendListItems(items, env, depth: 0, sb);
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
    private void AppendListItems(JsonElement items, string env, int depth, StringBuilder sb)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        var indent = new string(' ', depth * 2);
        sb.AppendLine($@"{indent}\begin{{{env}}}");
        foreach (var item in items.EnumerateArray())
        {
            var itemText = ExtractItemText(item);
            sb.AppendLine($@"{indent}\item {FormatInlineContent(itemText)}");
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

    private string RenderBlockquote(JsonElement content)
    {
        var text = GetText(content);
        return $@"\begin{{quote}}" + "\n" + FormatInlineContent(text) + "\n" + @"\end{quote}";
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

        // 1c. Native LaTeX commands users type directly (\cite, \ref,
        //     \eqref, \url, \href, \label, \footnote). Without these,
        //     EscapeLatex turns "\cite{X}" into "\textbackslash{}cite\{X\}"
        //     which renders as literal text in the PDF instead of a
        //     resolved citation. The placeholder preserves the command
        //     verbatim through the escape pass.
        result = Regex.Replace(result, @"\\cite\{([^}]+)\}", m => Ph($@"\cite{{{m.Groups[1].Value}}}"));
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
        result = Regex.Replace(result, @"\[%([\s\S]+?)%\]", m =>
        {
            var inner = m.Groups[1].Value;
            return Ph(inner.Contains('\n')
                ? $"\\begin{{comment}}\n{inner}\n\\end{{comment}}"
                : $"\\iffalse {inner}\\fi{{}}");
        });

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

    private static string BibStyleName(string style)
    {
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

    private record ProjectFile(string Path, string Content);
}
