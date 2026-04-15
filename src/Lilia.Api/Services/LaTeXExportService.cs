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

    public LaTeXExportService(
        LiliaDbContext context,
        IStorageService storageService,
        ILogger<LaTeXExportService> logger)
    {
        _context = context;
        _storageService = storageService;
        _logger = logger;
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
        sb.AppendLine($@"\documentclass[{options.FontSize},{options.PaperSize}]{{{options.DocumentClass}}}");
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

        // Main content
        var mainBlocks = blocks.Where(b => b.Type != "abstract" && b.Type != "bibliography").ToList();
        sb.AppendLine(BlocksToLatex(mainBlocks));

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
        main.AppendLine($@"\documentclass[{options.FontSize},{options.PaperSize}]{{{options.DocumentClass}}}");
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
        main.AppendLine($@"\documentclass[{options.FontSize},{options.PaperSize}]{{{options.DocumentClass}}}");
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

        // Use the shared preamble — same 31 packages as validation
        sb.Append(LaTeXPreamble.Packages);
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

        // Margins via geometry
        var marginParts = new List<string>();
        if (!string.IsNullOrEmpty(doc.MarginTop)) marginParts.Add($"top={doc.MarginTop}");
        if (!string.IsNullOrEmpty(doc.MarginBottom)) marginParts.Add($"bottom={doc.MarginBottom}");
        if (!string.IsNullOrEmpty(doc.MarginLeft)) marginParts.Add($"left={doc.MarginLeft}");
        if (!string.IsNullOrEmpty(doc.MarginRight)) marginParts.Add($"right={doc.MarginRight}");
        if (marginParts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("% Page margins");
            sb.AppendLine($@"\usepackage[{string.Join(",", marginParts)}]{{geometry}}");
        }

        // Line spacing (setspace already loaded in shared preamble)
        if (options.LineSpacing != 1.0)
        {
            sb.AppendLine();
            sb.AppendLine("% Line spacing");
            if (Math.Abs(options.LineSpacing - 1.5) < 0.01)
                sb.AppendLine(@"\onehalfspacing");
            else if (Math.Abs(options.LineSpacing - 2.0) < 0.01)
                sb.AppendLine(@"\doublespacing");
            else
                sb.AppendLine($@"\setstretch{{{options.LineSpacing}}}");
        }

        return sb.ToString();
    }

    // ── Block rendering ────────────────────────────────────────────────

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
                "tableOfContents" => @"\tableofcontents" + "\n" + @"\newpage",
                "pageBreak" => @"\newpage",
                "columnBreak" => @"\columnbreak",
                "embed" => RenderEmbed(content),
                "abstract" => "", // handled separately
                "bibliography" => "", // handled via .bib file
                _ => ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to LaTeX for export", block.Id);
            return $"% Error rendering block: {block.Id}";
        }
    }

    private string RenderParagraph(JsonElement content)
    {
        var text = GetText(content);
        return FormatInlineContent(text);
    }

    private string RenderHeading(JsonElement content)
    {
        var text = GetText(content);
        var level = content.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
        var commands = new[] { "section", "subsection", "subsubsection", "paragraph", "subparagraph" };
        var command = commands[Math.Min(level - 1, 4)];
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{{label}}}" : "";
        return $@"\{command}{{{EscapeLatex(text)}}}{labelPart}";
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

        var labelPart = !string.IsNullOrEmpty(label) ? $@"\label{{fig:{label}}}" : "";

        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{figure}[H]");
        sb.AppendLine(@"\centering");
        if (!string.IsNullOrEmpty(src))
        {
            var filename = ExtractImageFilename(src);
            sb.AppendLine($@"\includegraphics[width=0.8\textwidth]{{figures/{filename}}}");
        }
        else
        {
            sb.AppendLine(@"% [figure placeholder — no image uploaded]");
        }
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}{labelPart}");
        else if (!string.IsNullOrEmpty(labelPart))
            sb.AppendLine(labelPart);
        sb.Append(@"\end{figure}");
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

        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{table}[H]");
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
        sb.Append(@"\end{table}");
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
        sb.AppendLine($@"\begin{{{env}}}");

        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var itemText = ExtractItemText(item);
                sb.AppendLine($@"\item {FormatInlineContent(itemText)}");
            }
        }

        sb.Append($@"\end{{{env}}}");
        return sb.ToString();
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

        // 1. Inline math: $x^2$ → pass through as-is
        result = Regex.Replace(result, @"\$([^$]+)\$", m => Ph($"${m.Groups[1].Value}$"));

        // 2. Inline code: `text` → \texttt{text}
        result = Regex.Replace(result, @"`([^`]+)`", m => Ph($@"\texttt{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 3. Bold: *text* → \textbf{text}
        result = Regex.Replace(result, @"\*([^*]+)\*", m => Ph($@"\textbf{{{EscapeLatex(m.Groups[1].Value)}}}"));

        // 4. Italic: _text_ → \textit{text}
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

    private string GenerateBibTeXFile(List<BibliographyEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("% Bibliography file generated by Lilia");
        sb.AppendLine($"% Generated on {DateTime.UtcNow:O}");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            sb.AppendLine($"@{entry.EntryType ?? "article"}{{{entry.CiteKey},");

            try
            {
                var data = entry.Data?.RootElement;
                if (data.HasValue)
                {
                    AppendBibField(sb, data.Value, "author");
                    AppendBibField(sb, data.Value, "title");
                    AppendBibField(sb, data.Value, "year");
                    AppendBibField(sb, data.Value, "journal");
                    AppendBibField(sb, data.Value, "booktitle");
                    AppendBibField(sb, data.Value, "volume");
                    AppendBibField(sb, data.Value, "number");
                    AppendBibField(sb, data.Value, "pages");
                    AppendBibField(sb, data.Value, "publisher");
                    AppendBibField(sb, data.Value, "doi");
                    AppendBibField(sb, data.Value, "url");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse bibliography entry {CiteKey}", entry.CiteKey);
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendBibField(StringBuilder sb, JsonElement data, string field)
    {
        if (data.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
        {
            var value = val.GetString();
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($"  {field} = {{{value}}},");
        }
    }

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
