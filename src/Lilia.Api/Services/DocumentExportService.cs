using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lilia.Engines;
using Lilia.Engines.Themes;
using Lilia.Core.Blocks;

namespace Lilia.Api.Services;

public interface IDocumentExportService
{
    Task<ExportDocument> BuildExportDocumentAsync(Guid documentId);
    Task<byte[]> ExportToDocxAsync(Guid documentId);
    Task<byte[]> ExportToPdfAsync(Guid documentId);
    Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(Guid documentId, string? engineHint = null);

    /// <summary>
    /// The PDF with this export's look: <paramref name="lookTheme"/> (a theme id) and
    /// <paramref name="printSafe"/> override the document's stored look for this export only.
    /// </summary>
    Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(
        Guid documentId, string? engineHint, string? lookTheme, bool printSafe);
}

public class DocumentExportService : IDocumentExportService
{
    private readonly LiliaDbContext _context;
    private readonly IDocxExportService _docxExportService;
    private readonly IRenderService _renderService;
    private readonly ILaTeXExportService _latexExportService;
    private readonly ILaTeXRenderService _latexRenderService;
    private readonly IPreviewRenderService _previewRender;
    private readonly IStorageService _storageService;
    private readonly ILogger<DocumentExportService> _logger;

    public DocumentExportService(
        LiliaDbContext context,
        IDocxExportService docxExportService,
        IRenderService renderService,
        ILaTeXExportService latexExportService,
        ILaTeXRenderService latexRenderService,
        IPreviewRenderService previewRender,
        IStorageService storageService,
        ILogger<DocumentExportService> logger)
    {
        _context = context;
        _docxExportService = docxExportService;
        _renderService = renderService;
        _latexExportService = latexExportService;
        _latexRenderService = latexRenderService;
        _previewRender = previewRender;
        _storageService = storageService;
        _logger = logger;
    }

    private Func<string, string?>? _labelNumber;
    private Dictionary<Guid, string> _blockNumbers = [];
    private string? _currentBlockNumber;
    private string? _customPreamble;

    public async Task<ExportDocument> BuildExportDocumentAsync(Guid documentId)
    {
        var doc = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc == null)
            throw new KeyNotFoundException($"Document {documentId} not found");

        // The file's Author is the document's own author line, never the account id: that is an
        // internal identifier, and it travelled with every shared Word file (13 Oct sweep).
        var titleBlock = doc.Blocks.FirstOrDefault(b => b.Type == "title");
        var author = titleBlock is null ? null : NullIfEmpty(ProseLatex.PlainForWord(
            Regex.Replace(GetString(titleBlock.Content.RootElement, "author"), @"\s*\\and\b\s*", ", ")));

        var exportDoc = new ExportDocument
        {
            Title = doc.Title,
            Author = author,
            Language = doc.Language,
            PaperSize = doc.PaperSize,
            FontFamily = doc.FontFamily,
            FontSize = doc.FontSize,
            Columns = doc.Columns,
            BalancedColumns = doc.BalancedColumns,
            // The running header and footer (MCP-105: the .docx had none). The slots, or the legacy
            // header / footer text on the left, as the LaTeX export reads them.
            HeaderLeft = NullIfEmpty(doc.HeaderLeft) ?? (AllEmpty(doc.HeaderLeft, doc.HeaderCenter, doc.HeaderRight) ? NullIfEmpty(doc.HeaderText) : null),
            HeaderCenter = NullIfEmpty(doc.HeaderCenter),
            HeaderRight = NullIfEmpty(doc.HeaderRight),
            FooterLeft = NullIfEmpty(doc.FooterLeft) ?? (AllEmpty(doc.FooterLeft, doc.FooterCenter, doc.FooterRight) ? NullIfEmpty(doc.FooterText) : null),
            FooterCenter = NullIfEmpty(doc.FooterCenter),
            FooterRight = NullIfEmpty(doc.FooterRight),
            PageNumbering = doc.PageNumbering,
            Metadata = new ExportMetadata
            {
                Author = author,
                Created = doc.CreatedAt,
                Modified = doc.UpdatedAt
            }
        };

        // The document's entries, then what its bibliography blocks hold (BibTeX and @cite[key] text),
        // as the LaTeX export takes them (LaTeXExportService.WithTextEntries). MCP-98: the cited ones,
        // as BibTeX lists them, numbered in the order the references print; a citation prints its number.
        var allEntries = LaTeXExportService.WithTextEntries(doc.Blocks, doc.BibliographyEntries.ToList());
        var cited = CitedKeys(doc.Blocks);
        var listed = allEntries
            .Where(e => cited.Count == 0 || cited.Contains("*") || cited.Contains(e.CiteKey))
            .Select(ConvertBibliographyEntry)
            .OrderBy(e => BibTeXField.ShortAuthors(e.Fields.GetValueOrDefault("author")), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Fields.GetValueOrDefault("year") ?? "", StringComparer.Ordinal)
            .ThenBy(e => e.Fields.GetValueOrDefault("title") ?? e.CiteKey, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var n = 0; n < listed.Count; n++) listed[n].Number = n + 1;
        var citeNumbers = listed.ToDictionary(e => e.CiteKey, e => e.Number!.Value.ToString(), StringComparer.Ordinal);

        _customPreamble = doc.CustomPreamble;

        // References in prose take the numbers Word shows (MCP-96: they came from the last LaTeX compile,
        // or printed raw labels when there was none, and disagreed with Word's own), then the last
        // compile's for anything this count doesn't cover; citations ("cite:key") the numbers above.
        var labels = LabelNumbers.Parse(doc.LabelNumbers);
        var own = WordNumbers(doc);
        _blockNumbers = own.Blocks;
        _labelNumber = key => key.StartsWith("cite:", StringComparison.Ordinal)
            ? citeNumbers.GetValueOrDefault(key[5..])
            : own.Labels.TryGetValue(key, out var mine) ? mine
            : labels.TryGetValue(key, out var l) && !string.IsNullOrEmpty(l.Number) ? l.Number : null;

        // One counter per kind, as each \newtheorem has its own; a proof and numbered:false take
        // none. Word numbered every theorem-like block with one counter, proofs too ("Proof 4", MCP-105).
        var theoremCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in doc.Blocks)
        {
            int? theoremNumber = null;
            if (block.Type.ToLowerInvariant() == "theorem")
            {
                var kind = GetString(block.Content.RootElement, "theoremType");
                if (string.IsNullOrEmpty(kind)) kind = "theorem";
                var unnumbered = kind.Equals("proof", StringComparison.OrdinalIgnoreCase) || kind.EndsWith('*')
                    || (block.Content.RootElement.TryGetProperty("numbered", out var nb) && nb.ValueKind == JsonValueKind.False);
                if (!unnumbered) theoremNumber = theoremCounters[kind] = theoremCounters.GetValueOrDefault(kind) + 1;
            }

            _currentBlockNumber = _blockNumbers.GetValueOrDefault(block.Id);
            var exportBlock = await ConvertBlockAsync(block, theoremNumber);
            if (exportBlock != null)
                exportDoc.Blocks.Add(exportBlock);
        }

        if (listed.Count > 0) exportDoc.Bibliography = listed;

        return exportDoc;
    }

    /// <summary>
    /// The numbers the LaTeX export gives, counted as it counts them, for the Word file to show and
    /// its references to use: headings to the document's heading numbering, theorem-like blocks by
    /// kind (proofs and numbered:false unnumbered), numbered display equations (an align counts each
    /// line), captioned figures, tables, listings and algorithms.
    /// </summary>
    internal static (Dictionary<Guid, string> Blocks, Dictionary<string, string> Labels) WordNumbers(Document doc)
    {
        var blocks = new Dictionary<Guid, string>();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var depth = doc.HeadingNumbering switch { null => 3, < 0 => 3, var n => n.Value };
        var sections = new int[7];
        var kinds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var equations = 0;

        void Label(JsonElement content, string type, string number)
        {
            var label = GetString(content, "label");
            if (!string.IsNullOrWhiteSpace(label)) labels[LabelKey.Effective(type, label)] = number;
        }

        foreach (var block in doc.Blocks.OrderBy(b => b.SortOrder))
        {
            var content = block.Content.RootElement;
            switch (block.Type.ToLowerInvariant())
            {
                case "heading":
                {
                    var level = Math.Clamp(GetInt(content, "level", 1), 1, 6);
                    var unnumbered = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("numbered", out var nb) && nb.ValueKind == JsonValueKind.False;
                    if (unnumbered || level > depth) break;
                    sections[level]++;
                    for (var k = level + 1; k < sections.Length; k++) sections[k] = 0;
                    var number = string.Join(".", sections.Skip(1).Take(level).Select(x => Math.Max(x, 0)));
                    blocks[block.Id] = number;
                    Label(content, "heading", number);
                    break;
                }
                case "theorem":
                {
                    var kind = GetString(content, "theoremType");
                    if (string.IsNullOrEmpty(kind)) kind = "theorem";
                    if (kind.Equals("proof", StringComparison.OrdinalIgnoreCase) || kind.EndsWith('*')
                        || (content.TryGetProperty("numbered", out var tn) && tn.ValueKind == JsonValueKind.False)) break;
                    var number = (kinds[kind] = kinds.GetValueOrDefault(kind) + 1).ToString();
                    Label(content, "theorem", number);
                    break;
                }
                case "equation":
                {
                    var (mode, numbered) = Lilia.Core.Blocks.EquationContent.ReadMode(content);
                    if (!numbered || mode == "inline") break;
                    var source = Lilia.Core.Blocks.EquationContent.ReadSource(content);
                    var rows = mode is "align" or "gather" || Lilia.Core.Blocks.EquationContent.HasTopLevelAlignment(source)
                        ? Math.Max(1, Regex.Matches(source, @"\\\\(?!\s*$)").Count + 1 - Regex.Matches(source, @"\\(?:nonumber|notag)\b").Count)
                        : 1;
                    var number = (equations + 1).ToString();
                    equations += rows;
                    blocks[block.Id] = number;
                    Label(content, "equation", number);
                    break;
                }
                case "figure" or "image" or "table" or "code" or "algorithm":
                {
                    if (string.IsNullOrWhiteSpace(GetString(content, "caption"))) break;
                    var type = block.Type.ToLowerInvariant() == "image" ? "figure" : block.Type.ToLowerInvariant();
                    var number = (kinds["#" + type] = kinds.GetValueOrDefault("#" + type) + 1).ToString();
                    Label(content, type, number);
                    break;
                }
            }
        }
        return (blocks, labels);
    }

    private static readonly Regex CiteKeys = new(
        @"\\(?:no)?cite[a-zA-Z]*\*?(?:\[[^\]]*\]){0,2}\{(?<k>[^}]*)\}|@cite\{(?<k>[^}]*)\}|@cite\[(?<k>[^\]]*)\]",
        RegexOptions.Compiled);

    /// <summary>The keys the document's text cites (every string in its blocks but the bibliography).</summary>
    private static HashSet<string> CitedKeys(IEnumerable<Block> blocks)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    foreach (Match m in CiteKeys.Matches(el.GetString() ?? ""))
                        foreach (var k in m.Groups["k"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                            keys.Add(k);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject()) Walk(p.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var x in el.EnumerateArray()) Walk(x);
                    break;
            }
        }
        foreach (var b in blocks.Where(b => b.Type != "bibliography")) Walk(b.Content.RootElement);
        return keys;
    }

    public async Task<byte[]> ExportToDocxAsync(Guid documentId)
    {
        var exportDoc = await BuildExportDocumentAsync(documentId);
        return await _docxExportService.ExportAsync(exportDoc);
    }

    public async Task<byte[]> ExportToPdfAsync(Guid documentId)
    {
        var (pdf, _) = await ExportToPdfWithEngineAsync(documentId, null);
        return pdf;
    }

    public Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(Guid documentId, string? engineHint = null) =>
        ExportToPdfWithEngineAsync(documentId, engineHint, lookTheme: null, printSafe: false);

    public async Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(
        Guid documentId, string? engineHint, string? lookTheme, bool printSafe)
    {
        // engineHint = "typst"     → Typst only, throw if it fails
        // engineHint = "pdflatex"  → skip Typst, go straight to pdflatex
        // null / "auto" / anything else → existing behavior (Typst first,
        //                                  silent fallback to pdflatex)
        var hint = (engineHint ?? "auto").Trim().ToLowerInvariant();

        // The engine the document itself asks for. Read before the Typst branch
        // because it decides whether that branch may run at all.
        var documentEngine = await _context.Documents
            .Where(d => d.Id == documentId)
            .Select(d => d.LatexEngine)
            .FirstOrDefaultAsync() ?? "pdflatex";

        // Typst is a different typesetting system, not a faster pdflatex. It is
        // a fair default for a document on the default engine, and it is the
        // wrong answer for one that has deliberately chosen xelatex or lualatex
        // — those choices exist to get fontspec and system fonts, which Typst
        // does not reproduce. Such a document used to be handed a Typst PDF
        // anyway, its engine setting never consulted.
        //
        // So: an explicit LaTeX engine in the query skips Typst, and so does a
        // document that has chosen a non-default engine for itself.
        var wantsLatex = hint is "pdflatex" or "xelatex" or "lualatex"
                         || documentEngine is "xelatex" or "lualatex";

        // This export's look (Export PDF: Look ▾ / Print-safe). Null: the document's own.
        DocumentLook? lookOverride = null;
        if (!string.IsNullOrWhiteSpace(lookTheme) || printSafe)
        {
            var stored = await _context.Documents.AsNoTracking()
                .Where(d => d.Id == documentId).Select(d => d.Look).FirstOrDefaultAsync();
            lookOverride = DocumentLook.Parse(stored).With(lookTheme, printSafe);
        }

        // And a document whose page setup Typst would ignore (PageSetupRouting), unless the caller asked
        // for Typst by name. A themed look is one of those: the theme is a LaTeX package.
        if (!wantsLatex && hint != "typst")
        {
            var setupDoc = await _context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
            if (setupDoc is not null && lookOverride is not null) setupDoc.Look = lookOverride.ToStorage();
            if (setupDoc is not null && PageSetupRouting.WhyLatex(setupDoc) is not null) wantsLatex = true;
            // …and content Typst would print wrong (ContentRouting, MCP-80).
            if (!wantsLatex && ContentRouting.WhyLatex((await _context.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId)
                    .Select(b => new { b.Type, b.Content }).ToListAsync()).Select(b => (b.Type, b.Content.RootElement))) is not null)
                wantsLatex = true;
        }

        if (!wantsLatex)
        {
            // Phase 2 step 9 — Typst-first preview path. Sub-second compile
            // when it works; on any failure we silently fall through to the
            // existing pdflatex pipeline below (unless caller asked for
            // typst-only). Telemetry on every fallback is recorded inside
            // TryTypstPdfAsync so we see real-world coverage gaps.
            var typstPdf = await _previewRender.TryTypstPdfAsync(documentId);
            if (typstPdf is not null && typstPdf.Length > 0)
            {
                return (typstPdf, "typst");
            }
            if (hint == "typst")
            {
                throw new InvalidOperationException(
                    "Typst engine could not produce a PDF for this document. " +
                    "Try the pdflatex engine instead.");
            }
        }

        // pdflatex fallback — go through LaTeXExportService so PDF uses the
        // same preamble builder as the ZIP export — single source of truth
        // for class/options/packages filtering and the journal shims.
        var opts = new LaTeXExportOptions
        {
            Structure = "single",
            // The figures have to be in the project for the compile to find
            // them. With this false the PDF printed "[Missing figure: ...]" —
            // the \IfFileExists fallback around every \includegraphics — for
            // every image in the document.
            IncludeImages = true,
            DocumentClass = "article",
            // No FontSize / PaperSize: the document's own (they overrode it, so every LaTeX PDF was 11pt A4).
            // Compiled here: a theme this server cannot print fails the export with a message.
            CompileHere = true,
            LookOverride = lookOverride,
        };
        var projectStream = await _latexExportService.ExportToZipAsync(documentId, opts);
        using var archive = new System.IO.Compression.ZipArchive(projectStream, System.IO.Compression.ZipArchiveMode.Read);
        var mainEntry = archive.Entries.FirstOrDefault(e => e.Name == "main.tex");
        if (mainEntry == null)
            throw new InvalidOperationException("Generated LaTeX project has no main.tex");
        using var reader = new System.IO.StreamReader(mainEntry.Open());
        var latex = await reader.ReadToEndAsync();
        latex = await InlineBibliographyAsync(archive, latex, documentId);
        // Tolerant mode — body errors produce a partial PDF instead of 500.
        // Preamble errors still surface (no PDF file generated → exception).
        // Compile with the engine the document asks for. This passed no engine
        // at all, so a document set to xelatex or lualatex was compiled with
        // pdflatex regardless — silently, and with different output for anything
        // relying on fontspec or a system font. An explicit ?engine= wins; every
        // other hint ("auto", "typst") falls back to the document's own setting.
        var engine = hint is "xelatex" or "lualatex" or "pdflatex" ? hint : documentEngine;
        // …except Chinese, Japanese or Korean text, which the exporter sets with xeCJK: XeTeX only (MCP-26).
        if (EngineDetector.Detect(latex) == LatexEngine.Xelatex) engine = "xelatex";

        var (pdflatexPdf, aux) = await _latexRenderService.RenderToPdfTolerantWithAuxAsync(
            latex, timeout: 60, engine: engine,
            materialiseAssets: dir => WriteProjectAssetsAsync(archive, dir));
        await KeepLabelNumbersAsync(documentId, aux);
        return (pdflatexPdf, engine);
    }

    /// <summary>
    /// Keep the number every label got in this compile, dated, so the
    /// references panel can show them without compiling again — the numbers in
    /// the panel are then the ones in the PDF the author is looking at.
    ///
    /// <para>Best effort. The PDF is what was asked for; failing to record a
    /// by-product must never fail the export. And a compile that produced no
    /// labels leaves the last good numbers in place rather than blanking them,
    /// because the date beside them already says how old they are.</para>
    /// </summary>
    internal async Task KeepLabelNumbersAsync(Guid documentId, string? aux)
    {
        try
        {
            var stored = LabelNumbers.FromAux(aux);
            if (stored is null) return;

            var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
            if (doc is null) return;

            doc.LabelNumbers = stored;
            doc.LabelNumbersAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Export] could not keep label numbers for {DocumentId}", documentId);
        }
    }

    /// <summary>
    /// Write everything the project needs beside main.tex into the compile
    /// directory — figures above all.
    ///
    /// <para>Each entry is streamed from the archive straight to disk, so a
    /// paper with a dozen images never holds a dozen images in memory. The .tex
    /// is skipped: the caller has already modified it (the bibliography is
    /// inlined) and passes it separately.</para>
    ///
    /// <para>Entry names are checked against the destination even though this
    /// archive is one we generated ourselves. A zip that can write outside its
    /// own directory is a classic, and the check costs nothing.</para>
    /// </summary>
    private async Task WriteProjectAssetsAsync(
        System.IO.Compression.ZipArchive archive, string destinationDir)
    {
        var root = Path.GetFullPath(destinationDir);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;               // directory
            if (entry.FullName.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)) continue;

            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                _logger.LogWarning("[Export] Skipping archive entry outside the project: {Entry}",
                    entry.FullName);
                continue;
            }

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var source = entry.Open();
            await using var file = File.Create(target);
            await source.CopyToAsync(file);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex BibliographyCommandRe =
        new(@"\\bibliography\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Resolve <c>\bibliography{references}</c> into a literal
    /// <c>thebibliography</c> before the PDF compile.
    ///
    /// <para>The exported project keeps its bibliography in a separate
    /// references.bib, pulled in by <c>\bibliography{references}</c> — which
    /// only resolves if BibTeX runs between pdflatex passes.
    /// <c>RenderToPdfTolerantAsync</c> compiles a single string with two
    /// pdflatex passes and nothing else, and only main.tex was ever handed to
    /// it. references.bib never reached the compile directory, BibTeX never
    /// ran, and so <b>every citation in every exported PDF rendered as
    /// <c>[?]</c> and the References section came out empty</b> — the complaint
    /// that started this, on 2026-09-08. An author spent a session being told
    /// their document was at fault.</para>
    ///
    /// <para>This is the same move the arXiv export already makes: run the
    /// BibTeX cycle once via <see cref="ILaTeXRenderService.GenerateBblAsync"/>,
    /// then paste the resulting environment straight into the source so the
    /// two-pass compile can resolve the citations. Failure is non-fatal — a
    /// document still exports, just with the unresolved citations it had
    /// before.</para>
    /// </summary>
    private async Task<string> InlineBibliographyAsync(
        System.IO.Compression.ZipArchive archive, string latex, Guid documentId)
    {
        if (!BibliographyCommandRe.IsMatch(latex)) return latex;

        var files = new List<(string Path, string Content)>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!entry.FullName.EndsWith(".tex") && !entry.FullName.EndsWith(".bib")
                && !entry.FullName.EndsWith(".bst") && !entry.FullName.EndsWith(".sty")) continue;
            using var entryReader = new System.IO.StreamReader(entry.Open());
            files.Add((entry.FullName, await entryReader.ReadToEndAsync()));
        }

        // Nothing to cite — leave the source alone rather than pay for a
        // BibTeX round that can only come back empty.
        if (!files.Any(f => f.Path.EndsWith(".bib") && !string.IsNullOrWhiteSpace(f.Content)))
            return latex;

        var bbl = await _latexRenderService.GenerateBblAsync(files);
        if (string.IsNullOrWhiteSpace(bbl))
        {
            _logger.LogWarning(
                "[Export] BibTeX produced no bibliography for document {DocId}; "
                + "citations will render as [?] and References will be empty", documentId);
            return latex;
        }

        // MatchEvaluator, not a replacement string — a .bbl is full of $ and \,
        // which Regex.Replace would read as substitution syntax.
        return BibliographyCommandRe.Replace(latex, _ => bbl, 1);
    }

    private async Task<ExportBlock?> ConvertBlockAsync(Block block, int? theoremNumber)
    {
        var content = block.Content.RootElement;

        // If content is a plain string, wrap it as a paragraph
        if (content.ValueKind == JsonValueKind.String)
        {
            return new ExportBlock
            {
                Type = "paragraph",
                Content = new ExportBlockContent
                {
                    Text = content.GetString() ?? "",
                    RichText = ParseInlineFormatting(content.GetString() ?? "", _labelNumber)
                }
            };
        }

        var type = block.Type.ToLowerInvariant();

        // Handle legacy aliases
        type = type switch
        {
            "quote" => "blockquote",
            "image" => "figure",
            "divider" => "pagebreak",
            _ => type
        };

        return type switch
        {
            "paragraph" => ConvertParagraphBlock(content),
            "heading" => ConvertHeadingBlock(content),
            "equation" => ConvertEquationBlock(content),
            "code" => ConvertCodeBlock(content),
            "list" => ConvertListBlock(content),
            "table" => ConvertTableBlock(content),
            "figure" => await ConvertFigureBlockAsync(content, block.DocumentId),
            "blockquote" => ConvertBlockquoteBlock(content),
            "theorem" => ConvertTheoremBlock(content, theoremNumber),
            // Front and back matter: a preface, a dedication, an appendix (MCP-105: printed as text
            // without their title).
            "frontmatter" or "backmatter" => ConvertMatterBlock(content),
            "abstract" => ConvertAbstractBlock(content),
            "pagebreak" => ConvertPageBreakBlock(),
            "tableofcontents" => ConvertTableOfContentsBlock(),
            "algorithm" => ConvertAlgorithmBlock(content),
            "callout" => ConvertCalloutBlock(content),
            "footnote" => ConvertFootnoteBlock(content),
            // Passed through, not dropped. This returned null for "handled at
            // document level" — and no document level handled it, so the block
            // never reached the exporter and every Word export came out with no
            // References section (2026-09-09). The entries themselves were
            // being loaded and mapped onto ExportDocument.Bibliography the
            // whole time; only the block that marks where they go was missing.
            "bibliography" => new ExportBlock { Type = "bibliography" },
            // The title block carries the paper's title, author and date. It
            // had no case at all, so it fell through to null and every Word
            // export came out untitled and unattributed — a 24-block paper
            // exported starting at its abstract, with its author nowhere in the
            // file (2026-09-09).
            "title" => new ExportBlock
            {
                Type = "title",
                Content = new ExportBlockContent
                {
                    // Read as the PDF reads them: \and between authors is a comma (13 Oct sweep, LTX-56).
                    Text = ProseLatex.PlainForWord(GetString(content, "title"), _labelNumber),
                    Author = ProseLatex.PlainForWord(Regex.Replace(GetString(content, "author"), @"\s*\\and\b\s*", ", "), _labelNumber),
                    DateText = ProseLatex.PlainForWord(GetString(content, "date"), _labelNumber),
                }
            },
            _ => ConvertParagraphBlock(content) // fallback
        };
    }

    private ExportBlock ConvertParagraphBlock(JsonElement content)
    {
        // Content may be a string (legacy) or an object with a "text" property
        var text = content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? ""
            : GetString(content, "text");
        return new ExportBlock
        {
            Type = "paragraph",
            Content = new ExportBlockContent
            {
                Text = text,
                RichText = MapRichText(content, text)
            }
        };
    }

    /// <summary>
    /// The spans a paragraph is actually made of.
    ///
    /// <para>This used to be <c>ParseInlineFormatting(text)</c> unconditionally,
    /// which re-derived the formatting from the plain-text rendering and threw
    /// away the <c>richText</c> the editor had stored. Bold, italic, links,
    /// colour and highlight authored in Lilia all arrived in Word as plain
    /// text (2026-09-09).</para>
    ///
    /// <para>Stored spans win when present. Inline maths is still expanded
    /// inside each of them, carrying that span's marks onto the pieces, so a
    /// bold sentence containing <c>$x$</c> stays bold on both sides of the
    /// equation.</para>
    /// </summary>
    // Not static: references and citations take their numbers (_labelNumber); a paragraph printed
    // "Equation ([eq:x])" and "[key]" while lists and tables printed numbers (MCP-96, MCP-98).
    private List<ExportRichTextSpan> MapRichText(JsonElement content, string text)
    {
        if (content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("richText", out var stored)
            || stored.ValueKind != JsonValueKind.Array
            || stored.GetArrayLength() == 0)
        {
            return ParseInlineFormatting(text, _labelNumber);
        }

        var spans = new List<ExportRichTextSpan>();

        foreach (var element in stored.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var spanText = GetString(element, "text");
            var marks = new ExportRichTextSpan
            {
                Bold = GetBool(element, "bold", false),
                Italic = GetBool(element, "italic", false),
                Underline = GetBool(element, "underline", false),
                Strikethrough = GetBool(element, "strikethrough", false),
                Superscript = GetBool(element, "superscript", false),
                Subscript = GetBool(element, "subscript", false),
                Color = NullIfEmpty(GetString(element, "color")),
                Highlight = NullIfEmpty(GetString(element, "highlight")),
                Link = NullIfEmpty(GetString(element, "link")),
                FontSize = NullIfEmpty(GetString(element, "fontSize")),
                FontFamily = NullIfEmpty(GetString(element, "fontFamily")),
            };

            // An explicit equation on the span needs no further parsing.
            var explicitEquation = NullIfEmpty(GetString(element, "equation"));
            if (explicitEquation != null)
            {
                spans.Add(WithMarks(marks, spanText, explicitEquation));
                continue;
            }

            if (string.IsNullOrEmpty(spanText)) continue;

            foreach (var piece in ParseInlineFormatting(spanText, _labelNumber))
            {
                spans.Add(WithMarks(marks, piece.Text, piece.Equation));
            }
        }

        return spans.Count > 0 ? spans : ParseInlineFormatting(text, _labelNumber);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ExportRichTextSpan WithMarks(
        ExportRichTextSpan marks, string text, string? equation) => new()
    {
        Text = text,
        Equation = equation,
        Bold = marks.Bold,
        Italic = marks.Italic,
        Underline = marks.Underline,
        Strikethrough = marks.Strikethrough,
        Superscript = marks.Superscript,
        Subscript = marks.Subscript,
        Color = marks.Color,
        Highlight = marks.Highlight,
        Link = marks.Link,
        FontSize = marks.FontSize,
        FontFamily = marks.FontFamily,
    };

    private ExportBlock ConvertHeadingBlock(JsonElement content)
    {
        var text = GetString(content, "text");
        var level = GetInt(content, "level", 1);
        level = Math.Clamp(level, 1, 6);

        var plain = ProseLatex.PlainForWord(HeadingAnchor.WithoutTypedNumber(text), _labelNumber);
        return new ExportBlock
        {
            Type = "heading",
            Content = new ExportBlockContent
            {
                // Numbered as the PDF numbers it, so "Section 2" in the text finds "2 Methods" (MCP-96).
                Text = _currentBlockNumber is { } n ? $"{n} {plain}" : plain,
                Level = level
            }
        };
    }

    private ExportBlock ConvertEquationBlock(JsonElement content)
    {
        // "source" is the current key and "latex" the legacy one — the same
        // order DocxExportService.ConvertEquation coalesces in, and says so in
        // its own comment. This read "latex" then "text" and never "source", so
        // an equation block saved by the current editor arrived with nothing in
        // it and exported to Word as an italic "[]" (2026-09-09).
        var latex = GetString(content, "source");
        if (string.IsNullOrEmpty(latex))
            latex = GetString(content, "latex");
        if (string.IsNullOrEmpty(latex))
            latex = GetString(content, "text");

        var displayMode = GetBool(content, "displayMode", true);

        return new ExportBlock
        {
            Type = "equation",
            Content = new ExportBlockContent
            {
                Latex = AsWordDisplay(ProseLatex.MathForWord(latex)),
                DisplayMode = displayMode,
                // "(3)" at the right margin, as the PDF prints it; Word's equations had no number (MCP-96).
                EquationNumber = _currentBlockNumber,
            }
        };
    }

    /// <summary>
    /// A multi-line body (<c>a &amp;= b \\ c &amp;= d</c>, what an align or gather block stores) as
    /// one aligned display. Word's converter stopped at the first <c>\\</c>, so an align* kept
    /// only its first line (MCP-94, run 3).
    /// </summary>
    internal static string AsWordDisplay(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return latex;
        if (!Lilia.Core.Blocks.EquationContent.HasTopLevelAlignment(latex) && !HasTopLevelRowBreak(latex)) return latex;
        var body = Regex.Replace(latex.Trim(), @"\\\\\s*$", "");
        return $"\\begin{{aligned}}{body}\\end{{aligned}}";
    }

    private static bool HasTopLevelRowBreak(string s)
    {
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\') continue;
            if (string.CompareOrdinal(s, i, @"\begin{", 0, 7) == 0) { depth++; i += 6; continue; }
            if (string.CompareOrdinal(s, i, @"\end{", 0, 5) == 0) { if (depth > 0) depth--; i += 4; continue; }
            if (i + 1 < s.Length && s[i + 1] == '\\' && depth == 0) return true;
            i++;
        }
        return false;
    }

    private ExportBlock ConvertCodeBlock(JsonElement content)
    {
        var code = GetString(content, "code");
        if (string.IsNullOrEmpty(code))
            code = GetString(content, "text");

        return new ExportBlock
        {
            Type = "code",
            Content = new ExportBlockContent
            {
                Code = code,
                Language = GetString(content, "language"),
                Caption = ProseLatex.PlainForWord(GetString(content, "caption"), _labelNumber),
                CaptionRichText = CaptionRuns(GetString(content, "caption")),
            }
        };
    }

    private ExportBlock ConvertListBlock(JsonElement content)
    {
        // One reading of kind / listType / ordered for every emitter (ListKind): "numbered" and
        // kind "description" were lost here (MCP-95, MCP-99).
        var listType = ListKind.Of(content) switch
        {
            ListKind.Numbered => "ordered",
            ListKind.Description => "description",
            _ => "bullet",
        };

        var items = new List<ExportListItem>();
        if (content.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsEl.EnumerateArray())
            {
                items.Add(ConvertListItem(item));
            }
        }

        return new ExportBlock
        {
            Type = "list",
            Content = new ExportBlockContent
            {
                Items = items,
                ListType = listType
            }
        };
    }

    private ExportListItem ConvertListItem(JsonElement item)
    {
        string text;
        if (item.ValueKind == JsonValueKind.String)
        {
            text = item.GetString() ?? "";
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            text = GetString(item, "text");
            if (string.IsNullOrEmpty(text) && item.TryGetProperty("richText", out var rt) && rt.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var span in rt.EnumerateArray())
                {
                    if (span.TryGetProperty("text", out var st))
                        parts.Add(st.GetString() ?? "");
                }
                text = string.Join("", parts);
            }
        }
        else
        {
            text = "";
        }

        // A description item: {text: term, description}.
        var description = item.ValueKind == JsonValueKind.Object ? GetString(item, "description") : "";
        var exportItem = new ExportListItem
        {
            Text = text,
            RichText = ParseInlineFormatting(text, _labelNumber),
            Description = string.IsNullOrEmpty(description) ? null : ParseInlineFormatting(description, _labelNumber),
        };

        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array &&
            children.GetArrayLength() > 0)
        {
            exportItem.Children = new List<ExportListItem>();
            foreach (var child in children.EnumerateArray())
            {
                exportItem.Children.Add(ConvertListItem(child));
            }
        }

        return exportItem;
    }

    private ExportBlock ConvertTableBlock(JsonElement content)
    {
        var hasHeader = GetBool(content, "hasHeader", true);

        // The editor stores a table as { headers: [...], rows: [[...]] } — the
        // column headings live in their own array, not as the first entry of
        // rows. Only "rows" was read, so every real table exported to Word with
        // its header row missing (2026-09-09).
        var headers = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("headers", out var headersEl)
            && headersEl.ValueKind == JsonValueKind.Array && headersEl.GetArrayLength() > 0
            && headersEl.EnumerateArray().Any(h => !string.IsNullOrWhiteSpace(TableColumnFormat.CellText(h)))
            ? headersEl : (JsonElement?)null;
        var bodyRows = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array
            ? rowsEl.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Array).ToList()
            : [];
        if (headers is not null) hasHeader = true;

        // MCP-93 (run 3): cells written as objects ({"content": …, "rowspan": 2}) exported empty —
        // only {text} was read — and rows came out ragged, with no spans, notes or group headers.
        // The grid is built as the LaTeX emitter builds it (RenderService table path, TableLayout).
        static int Span(JsonElement cell, string name) =>
            cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) && n > 1 ? n : 1;
        var stored = (headers is { } hd ? new[] { hd } : []).Concat(bodyRows).ToList();
        var colCount = stored.Count == 0 ? 0 : stored.Max(r => r.EnumerateArray().Sum(c => Span(c, "colspan")));
        var layout = TableLayout.Read(content, colCount, headers is not null);

        var rows = new List<List<ExportTableCell>>();
        var covered = new Dictionary<(int Row, int Col), int>();
        for (var g = 0; g < stored.Count; g++)
        {
            var isHeader = headers is not null && g == 0;
            var layoutRow = headers is null ? g : g - 1;
            var row = new List<ExportTableCell>();
            var col = 0;
            void FillCovered()
            {
                while (col < colCount && covered.TryGetValue((g, col), out var span))
                {
                    row.Add(new ExportTableCell { MergedAbove = true, ColSpan = span });
                    col += span;
                }
            }
            var ci = 0;
            foreach (var cell in stored[g].EnumerateArray())
            {
                var index = ci++;
                FillCovered();
                if (col >= colCount) break;
                var text = TableColumnFormat.CellText(cell);
                var colspan = Math.Min(Span(cell, "colspan"), colCount - col);
                var rowspan = Span(cell, "rowspan");
                var rich = ParseInlineFormatting(text, _labelNumber) ?? [];
                if (layout.MarkAt(isHeader ? -1 : layoutRow, index) is { } mark)
                    rich.Add(new ExportRichTextSpan { Text = mark, Superscript = true });
                row.Add(new ExportTableCell
                {
                    Text = ProseLatex.PlainForWord(text, _labelNumber),
                    RichText = rich.Count > 0 ? rich : null,
                    ColSpan = colspan,
                    RowSpan = Math.Min(rowspan, stored.Count - g),
                });
                for (var k = 1; k < rowspan && g + k < stored.Count; k++) covered[(g + k, col)] = colspan;
                col += colspan;
            }
            // A short row is padded to the table's width, or Word shifts its cells under the wrong columns.
            while (col < colCount)
            {
                FillCovered();
                if (col >= colCount) break;
                row.Add(new ExportTableCell());
                col++;
            }
            rows.Add(row);
        }

        // Group headers: a row above the header, each label over its columns.
        var headerRows = headers is not null || hasHeader ? 1 : 0;
        if (headers is not null && layout.Groups.Count > 0)
        {
            var groupRow = new List<ExportTableCell>();
            var col = 0;
            foreach (var group in layout.Groups)
            {
                for (; col < group.Start; col++) groupRow.Add(new ExportTableCell());
                groupRow.Add(new ExportTableCell { Text = ProseLatex.PlainForWord(group.Label, _labelNumber), RichText = ParseInlineFormatting(group.Label, _labelNumber), ColSpan = group.Span });
                col += group.Span;
            }
            for (; col < colCount; col++) groupRow.Add(new ExportTableCell());
            rows.Insert(0, groupRow);
            headerRows = 2;
        }

        return new ExportBlock
        {
            Type = "table",
            Content = new ExportBlockContent
            {
                Rows = rows,
                HasHeader = hasHeader,
                HeaderRows = headerRows,
                TableNotes = layout.HasNotes ? layout.Notes.Select(n => (n.Mark, ProseLatex.PlainForWord(n.Text, _labelNumber))).ToList() : null,
                // Dropped until 26 Sep: every other export printed the table's
                // caption; the .docx had none.
                Caption = ProseLatex.PlainForWord(GetString(content, "caption"), _labelNumber),
                CaptionRichText = CaptionRuns(GetString(content, "caption")),
            }
        };
    }

    private async Task<ExportBlock> ConvertFigureBlockAsync(JsonElement content, Guid documentId)
    {
        var src = GetString(content, "src");
        var alt = GetString(content, "alt");
        var caption = GetString(content, "caption");
        var width = content.TryGetProperty("width", out var w) ? w.GetDouble() : 0.8;

        ExportImageData? imageData = null;

        // An image object carrying base64 directly — what an upload produces.
        // Checked before the URL path so an embedded image never triggers a
        // network fetch.
        if (content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("image", out var embedded)
            && embedded.ValueKind == JsonValueKind.Object)
        {
            var data = GetString(embedded, "data");
            if (!string.IsNullOrWhiteSpace(data))
            {
                imageData = new ExportImageData
                {
                    Data = StripDataUriPrefix(data),
                    MimeType = NullIfEmpty(GetString(embedded, "mimeType")) ?? "image/png",
                    Filename = NullIfEmpty(GetString(embedded, "filename")),
                };
            }
        }

        // A data: URI in src — the shape the editor writes for a pasted or
        // uploaded image. DownloadImageAsync speaks HTTP only, so these used to
        // fall straight through and the figure exported with no image at all.
        if (imageData == null && src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            imageData = ImageFromDataUri(src);
        }

        // Anything else must be an asset belonging to THIS document, resolved
        // by database lookup and read straight out of storage. See
        // ResolveDocumentAssetAsync for why the URL is never fetched.
        if (imageData == null && !string.IsNullOrEmpty(src))
        {
            imageData = await ResolveDocumentAssetAsync(documentId, src);
        }

        // A TikZ figure has no file: draw it (MCP-104: it exported as its caption only).
        if (imageData == null && TikzFigure.IsTikz(content))
            imageData = await RenderTikzAsync(TikzFigure.Source(content));

        if (imageData != null)
        {
            imageData.AltText = alt;
            imageData.Width = width * 500; // approximate pixel width
            // A PNG's own proportions (its IHDR header); 4:3 for anything else.
            imageData.Height = imageData.Width * (PngAspect(imageData) ?? 0.75);
        }
        else if (!string.IsNullOrWhiteSpace(alt))
        {
            // No image, but its description: the placeholder says what the figure shows.
            imageData = new ExportImageData { AltText = alt };
        }

        return new ExportBlock
        {
            Type = "figure",
            Content = new ExportBlockContent
            {
                Image = imageData,
                Caption = ProseLatex.PlainForWord(caption, _labelNumber),
                CaptionRichText = CaptionRuns(caption),
            }
        };
    }

    /// <summary>
    /// A TikZ picture as a PNG: a standalone page with the packages the picture needs and the
    /// document's own TikZ setup (\usetikzlibrary, \tikzset…). Null when it doesn't compile, or the
    /// source is refused, and the writer prints a placeholder.
    /// </summary>
    private async Task<ExportImageData?> RenderTikzAsync(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var sb = new System.Text.StringBuilder();
        sb.Append("\\documentclass[border=4pt]{standalone}\n\\usepackage[T1]{fontenc}\n\\usepackage{amsmath,amssymb}\n\\usepackage{xcolor}\n");
        foreach (var pkg in TikzFigure.RequiredPackages(source)) sb.Append("\\usepackage{").Append(pkg).Append("}\n");
        if (TikzFigure.RequiredPackages(source).Contains("pgfplots")) sb.Append("\\pgfplotsset{compat=1.18}\n");
        sb.Append(TikzFigure.SetupLines(_customPreamble)).Append('\n');
        sb.Append("\\begin{document}\n").Append(source).Append("\n\\end{document}\n");
        var tex = sb.ToString();
        if (Lilia.Engines.TexSafety.TexSourceGuard.Violation(tex) is not null) return null;
        try
        {
            var png = await _latexRenderService.RenderToPngAsync(tex, dpi: 200, timeout: 30);
            return png is { Length: > 0 }
                ? new ExportImageData { Data = Convert.ToBase64String(png), MimeType = "image/png", Filename = "tikz.png" }
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[Export] a TikZ figure did not render for Word");
            return null;
        }
    }

    /// <summary>Height over width from a PNG's IHDR chunk, or null when it isn't a PNG.</summary>
    private static double? PngAspect(ExportImageData image)
    {
        if (!string.Equals(image.MimeType, "image/png", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(image.Data)) return null;
        try
        {
            var head = Convert.FromBase64String(image.Data.Length > 64 ? image.Data[..64] : image.Data);
            if (head.Length < 24 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G') return null;
            var w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            var h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return w > 0 && h > 0 ? (double)h / w : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private ExportBlock ConvertBlockquoteBlock(JsonElement content)
    {
        var text = GetString(content, "text");
        var attribution = GetString(content, "attribution");
        return new ExportBlock
        {
            Type = "blockquote",
            Content = new ExportBlockContent
            {
                Text = text,
                RichText = ParseInlineFormatting(text, _labelNumber),
                Variant = NullIfEmpty(GetString(content, "variant")),
                // The attribution was dropped (MCP-105).
                Attribution = NullIfEmpty(ProseLatex.PlainForWord(attribution, _labelNumber)),
            }
        };
    }

    private ExportBlock ConvertTheoremBlock(JsonElement content, int? theoremNumber)
    {
        var theoremType = GetString(content, "theoremType");
        if (string.IsNullOrEmpty(theoremType)) theoremType = "theorem";

        var title = GetString(content, "title");
        var text = GetString(content, "text");

        return new ExportBlock
        {
            Type = "theorem",
            Content = new ExportBlockContent
            {
                TheoremType = theoremType,
                TheoremNumber = theoremNumber,
                Text = ProseLatex.PlainForWord(title, _labelNumber),  // title goes in Text for the header
                RichText = ParseInlineFormatting(text, _labelNumber) // body text in RichText
            }
        };
    }

    private ExportBlock ConvertMatterBlock(JsonElement content)
    {
        var subType = GetString(content, "subType");
        if (string.IsNullOrEmpty(subType)) subType = GetString(content, "kind");
        var title = GetString(content, "title");
        if (string.IsNullOrEmpty(title))
            title = subType.ToLowerInvariant() switch
            {
                "" or "dedication" => "",
                "acknowledgments" or "acknowledgements" => "Acknowledgements",
                _ => char.ToUpperInvariant(subType[0]) + subType[1..],
            };
        var text = GetString(content, "text");
        if (string.IsNullOrEmpty(text)) text = GetString(content, "content");
        return new ExportBlock
        {
            Type = "matter",
            Content = new ExportBlockContent
            {
                Variant = subType.ToLowerInvariant(),
                Text = ProseLatex.PlainForWord(title, _labelNumber),
                RichText = string.IsNullOrWhiteSpace(text) ? null : ParseInlineFormatting(text, _labelNumber),
            }
        };
    }

    private static bool AllEmpty(params string?[] values) => values.All(string.IsNullOrWhiteSpace);

    private ExportBlock ConvertAbstractBlock(JsonElement content)
    {
        var text = GetString(content, "text");
        return new ExportBlock
        {
            Type = "abstract",
            Content = new ExportBlockContent
            {
                Text = text,
                RichText = ParseInlineFormatting(text, _labelNumber)
            }
        };
    }

    private ExportBlock ConvertPageBreakBlock()
    {
        return new ExportBlock
        {
            Type = "pageBreak",
            Content = new ExportBlockContent()
        };
    }

    private ExportBlock ConvertTableOfContentsBlock()
    {
        return new ExportBlock
        {
            Type = "tableOfContents",
            Content = new ExportBlockContent()
        };
    }

    private ExportBlock ConvertAlgorithmBlock(JsonElement content)
    {
        var title = GetString(content, "title");
        // "code" is the usual key; "text" and "content" appear in documents
        // written by other paths. Reading only "code" meant an algorithm block
        // exported as an empty caption with its steps gone.
        // Readable pseudocode from whichever shape the block holds (AlgorithmText): typed lines were
        // dropped, and a code string of \STATE / \FOR printed as typed.
        var code = AlgorithmText.ForWord(content);
        var caption = GetString(content, "caption");

        if (string.IsNullOrEmpty(caption) && !string.IsNullOrEmpty(title))
            caption = $"Algorithm: {title}";

        return new ExportBlock
        {
            Type = "algorithm",
            Content = new ExportBlockContent
            {
                Code = code,
                Caption = ProseLatex.PlainForWord(caption, _labelNumber),
                CaptionRichText = CaptionRuns(caption),
                Language = "text"
            }
        };
    }

    private ExportBlock ConvertCalloutBlock(JsonElement content)
    {
        var title = GetString(content, "title");
        var text = GetString(content, "text");

        return new ExportBlock
        {
            Type = "callout",
            Content = new ExportBlockContent
            {
                Text = ProseLatex.PlainForWord(title, _labelNumber),
                RichText = ParseInlineFormatting(text, _labelNumber)
            }
        };
    }

    private ExportBlock ConvertFootnoteBlock(JsonElement content)
    {
        var text = GetString(content, "text");
        return new ExportBlock
        {
            Type = "footnote",
            Content = new ExportBlockContent
            {
                Text = text,
                RichText = ParseInlineFormatting(text, _labelNumber)
            }
        };
    }

    private ExportBibliographyEntry ConvertBibliographyEntry(BibliographyEntry entry)
    {
        var fields = new Dictionary<string, string>();
        if (entry.Data?.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in entry.Data.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    fields[prop.Name] = prop.Value.GetString() ?? "";
                else
                    fields[prop.Name] = prop.Value.ToString();
            }
        }

        return new ExportBibliographyEntry
        {
            CiteKey = entry.CiteKey,
            EntryType = entry.EntryType,
            // BibTeX markup as the reader sees it: Fej{\'e}r → Fejér, -- → –, {NLP} → NLP (MCP-98).
            Fields = fields.ToDictionary(f => f.Key, f => BibTeXField.ToPlain(f.Value)),
        };
    }

    /// <summary>Everything after the base64 comma in a data: URI.</summary>
    private static string StripDataUriPrefix(string data)
    {
        if (!data.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return data;
        var comma = data.IndexOf(',');
        return comma >= 0 ? data[(comma + 1)..] : data;
    }

    /// <summary>
    /// An image embedded in the document rather than hosted somewhere.
    /// <c>data:image/png;base64,iVBOR...</c> — no network involved.
    /// </summary>
    private static ExportImageData? ImageFromDataUri(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0) return null;

        var header = uri[5..comma];          // "image/png;base64"
        var payload = uri[(comma + 1)..];
        if (string.IsNullOrWhiteSpace(payload)) return null;

        var semicolon = header.IndexOf(';');
        var mime = (semicolon >= 0 ? header[..semicolon] : header).Trim();

        return new ExportImageData
        {
            Data = payload,
            MimeType = string.IsNullOrWhiteSpace(mime) ? "image/png" : mime,
        };
    }

    /// <summary>
    /// The bytes behind a figure's <c>src</c>, if and only if they belong to
    /// this document.
    ///
    /// <para>This used to be <c>DownloadImageAsync</c>: it took the <c>src</c>
    /// string out of the block's JSON and issued an HTTP GET to it. That made
    /// exporting a document a request the server performed on behalf of
    /// whoever wrote the document — server-side request forgery with a pleasant
    /// user interface. A document containing
    /// <c>http://169.254.169.254/latest/meta-data/</c> or
    /// <c>http://127.0.0.1:5432/</c> would have had the API fetch it, from
    /// inside the network, whenever the author chose.</para>
    ///
    /// <para>The rule now is: <b>no URL is ever fetched.</b> An image exports if
    /// it is embedded in the block, or if it is an asset row belonging to this
    /// document — read from storage by its <c>StorageKey</c>, a value the server
    /// wrote and the author cannot influence. Scoping the lookup to the document
    /// also stops one document pulling in another's assets by quoting its
    /// URL.</para>
    ///
    /// <para>An unresolvable image is skipped and the figure keeps its caption.
    /// Losing a picture is a visible, recoverable loss; fetching an arbitrary
    /// URL is not.</para>
    /// </summary>
    private async Task<ExportImageData?> ResolveDocumentAssetAsync(Guid documentId, string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;

        var asset = await _context.Assets
            .Where(a => a.DocumentId == documentId)
            .FirstOrDefaultAsync(a => a.Url == src);

        if (asset != null)
            return await ReadAssetAsync(asset.StorageKey, asset.FileType, asset.FileName, asset.FileSize);

        // Fall back to the storage key appearing in the URL: a public URL may be
        // rewritten (CDN host, signature) after the block was written, but the
        // key inside it does not move.
        var candidates = await _context.Assets
            .Where(a => a.DocumentId == documentId)
            .Select(a => new { a.StorageKey, a.FileType, a.FileName, a.FileSize })
            .ToListAsync();

        var match = candidates.FirstOrDefault(
            a => !string.IsNullOrEmpty(a.StorageKey)
                 && src.Contains(a.StorageKey, StringComparison.Ordinal));

        if (match == null)
        {
            _logger.LogInformation(
                "[Export] Figure src for document {DocId} matches no asset of that document; "
                + "skipping the image. Remote URLs are not fetched.", documentId);
            return null;
        }

        return await ReadAssetAsync(match.StorageKey, match.FileType, match.FileName, match.FileSize);
    }

    /// <summary>Ten megabytes — the same limit the asset passed to be uploaded.</summary>
    private const long MaxEmbeddableAssetBytes = 10L * 1024 * 1024;

    private async Task<ExportImageData?> ReadAssetAsync(
        string storageKey, string? fileType, string? fileName, long fileSize)
    {
        if (string.IsNullOrEmpty(storageKey)) return null;

        if (fileSize > MaxEmbeddableAssetBytes)
        {
            _logger.LogWarning(
                "[Export] Asset {Key} is {Bytes} bytes, above the embedding limit; skipping.",
                storageKey, fileSize);
            return null;
        }

        try
        {
            await using var stream = await _storageService.DownloadAsync(storageKey);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            if (buffer.Length == 0 || buffer.Length > MaxEmbeddableAssetBytes) return null;

            return new ExportImageData
            {
                Data = Convert.ToBase64String(buffer.ToArray()),
                MimeType = string.IsNullOrWhiteSpace(fileType) ? "image/png" : fileType,
                Filename = fileName,
            };
        }
        catch (Exception ex)
        {
            // Storage being unavailable must not cost the whole document.
            _logger.LogWarning(ex, "[Export] Could not read asset {Key}; skipping the image.", storageKey);
            return null;
        }
    }

    #region Inline Formatting Parser

    /// <summary>
    /// Parse markdown-like inline formatting into rich text spans.
    /// Supports: **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, $math$
    /// </summary>
    public static List<ExportRichTextSpan> ParseInlineFormatting(string? text) => ParseInlineFormatting(text, null);

    /// <summary>
    /// The same, after the LaTeX in the prose is read for Word (<see cref="ProseLatex"/>):
    /// <paramref name="labelNumber"/> numbers references from the last compile.
    /// </summary>
    public static List<ExportRichTextSpan> ParseInlineFormatting(string? text, Func<string, string?>? labelNumber)
    {
        var spans = ParseMarks(ProseLatex.ForWord(text, labelNumber));
        foreach (var span in spans)
        {
            if (span.Equation is null && span.Text.Contains(ProseLatex.PriceDollar))
                span.Text = span.Text.Replace(ProseLatex.PriceDollar, '$');
            if (span.Equation is not null)
                span.Equation = ProseLatex.MathForWord(span.Equation);
        }
        return spans;
    }

    /// <summary>A caption's runs, or null when it is plain text.</summary>
    private List<ExportRichTextSpan>? CaptionRuns(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return null;
        var spans = ParseInlineFormatting(caption, _labelNumber);
        return spans.Count == 1 && spans[0].Equation is null && !spans[0].Bold && !spans[0].Italic && !spans[0].Superscript
            && !spans[0].Subscript && spans[0].Highlight is null ? null : spans;
    }

    private static List<ExportRichTextSpan> ParseMarks(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [new ExportRichTextSpan { Text = text ?? "" }];

        var spans = new List<ExportRichTextSpan>();
        var i = 0;
        var currentText = new System.Text.StringBuilder();

        void FlushCurrent()
        {
            if (currentText.Length > 0)
            {
                spans.Add(new ExportRichTextSpan { Text = currentText.ToString() });
                currentText.Clear();
            }
        }

        while (i < text.Length)
        {
            // Display math: $$...$$
            //
            // This ran second to the single-$ case below, which explicitly
            // declines to match when the next character is also '$' — so a
            // paragraph containing $$a^2 + b^2 = c^2$$ matched neither branch
            // and the dollars reached Word as literal text (2026-09-09).
            //
            // It becomes an equation span like any other. A display equation
            // sitting inside a paragraph is still typeset inline rather than
            // centred on its own line; splitting the paragraph around it is a
            // larger change than this, and literal "$$" was the actual defect.
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '$')
            {
                var closer = text.IndexOf("$$", i + 2, StringComparison.Ordinal);
                if (closer > i + 2)
                {
                    FlushCurrent();
                    var latex = text[(i + 2)..closer];
                    spans.Add(new ExportRichTextSpan { Text = latex, Equation = latex });
                    i = closer + 2;
                    continue;
                }
            }

            // Inline math: $...$
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] != '$')
            {
                var end = text.IndexOf('$', i + 1);
                if (end > i + 1)
                {
                    FlushCurrent();
                    spans.Add(new ExportRichTextSpan
                    {
                        Text = text[(i + 1)..end],
                        Equation = text[(i + 1)..end]
                    });
                    i = end + 1;
                    continue;
                }
            }

            // Bold: **...**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    FlushCurrent();
                    spans.Add(new ExportRichTextSpan
                    {
                        Text = text[(i + 2)..end],
                        Bold = true
                    });
                    i = end + 2;
                    continue;
                }
            }

            // Underline: __...__
            if (i + 1 < text.Length && text[i] == '_' && text[i + 1] == '_')
            {
                var end = text.IndexOf("__", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    FlushCurrent();
                    spans.Add(new ExportRichTextSpan
                    {
                        Text = text[(i + 2)..end],
                        Underline = true
                    });
                    i = end + 2;
                    continue;
                }
            }

            // Lilia's marks the LaTeX emitter reads too (RenderService): ==highlight==, ^^small caps^^,
            // ^superscript^, %%subscript%%. They printed as typed in Word (MCP-103).
            if (Mark(text, i, "==", out var hl))
            {
                FlushCurrent();
                spans.Add(new ExportRichTextSpan { Text = hl, Highlight = "yellow" });
                i += hl.Length + 4;
                continue;
            }
            if (Mark(text, i, "^^", out var sc))
            {
                FlushCurrent();
                // Word's small capitals would need a run property the writer doesn't set; the text stays.
                spans.Add(new ExportRichTextSpan { Text = sc });
                i += sc.Length + 4;
                continue;
            }
            if (text[i] == '^' && (i == 0 || text[i - 1] != '^') && Mark(text, i, "^", out var sup) && !sup.StartsWith(' '))
            {
                FlushCurrent();
                spans.Add(new ExportRichTextSpan { Text = sup, Superscript = true });
                i += sup.Length + 2;
                continue;
            }
            if (Mark(text, i, "%%", out var sub))
            {
                FlushCurrent();
                spans.Add(new ExportRichTextSpan { Text = sub, Subscript = true });
                i += sub.Length + 4;
                continue;
            }

            // Italic: *...*
            if (text[i] == '*' && (i + 1 < text.Length && text[i + 1] != '*'))
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i + 1)
                {
                    FlushCurrent();
                    spans.Add(new ExportRichTextSpan
                    {
                        Text = text[(i + 1)..end],
                        Italic = true
                    });
                    i = end + 1;
                    continue;
                }
            }

            // Strikethrough: ~~...~~
            if (i + 1 < text.Length && text[i] == '~' && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    FlushCurrent();
                    spans.Add(new ExportRichTextSpan
                    {
                        Text = text[(i + 2)..end],
                        Strikethrough = true
                    });
                    i = end + 2;
                    continue;
                }
            }

            // Inline code: `...`
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    FlushCurrent();
                    spans.Add(new ExportRichTextSpan
                    {
                        Text = text[(i + 1)..end],
                        FontFamily = "Consolas"
                    });
                    i = end + 1;
                    continue;
                }
            }

            currentText.Append(text[i]);
            i++;
        }

        FlushCurrent();

        // If no formatting was found, return a single span
        if (spans.Count == 0)
            spans.Add(new ExportRichTextSpan { Text = text });

        return spans;
    }

    /// <summary>A mark opened by <paramref name="fence"/> at <paramref name="i"/> and closed by it on the
    /// same line, with something between (no ^ inside a ^…^ superscript).</summary>
    private static bool Mark(string text, int i, string fence, out string inner)
    {
        inner = "";
        if (string.CompareOrdinal(text, i, fence, 0, fence.Length) != 0) return false;
        if (fence == "^" && i + 1 < text.Length && text[i + 1] == '^') return false;
        var close = text.IndexOf(fence, i + fence.Length, StringComparison.Ordinal);
        if (close <= i + fence.Length) return false;
        var body = text[(i + fence.Length)..close];
        if (body.Contains('\n') || (fence == "^" && (body.Contains(' ') || (close + 1 < text.Length && text[close + 1] == '^')))) return false;
        inner = body;
        return true;
    }

    #endregion

    #region JSON Helpers

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? ""
            : "";
    }

    private static int GetInt(JsonElement element, string property, int defaultValue = 0)
    {
        if (element.TryGetProperty(property, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number)
                return val.GetInt32();
        }
        return defaultValue;
    }

    private static bool GetBool(JsonElement element, string property, bool defaultValue = false)
    {
        if (element.TryGetProperty(property, out var val))
        {
            if (val.ValueKind == JsonValueKind.True) return true;
            if (val.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    #endregion
}
