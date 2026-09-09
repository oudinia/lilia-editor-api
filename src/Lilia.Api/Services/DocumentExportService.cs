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

namespace Lilia.Api.Services;

public interface IDocumentExportService
{
    Task<ExportDocument> BuildExportDocumentAsync(Guid documentId);
    Task<byte[]> ExportToDocxAsync(Guid documentId);
    Task<byte[]> ExportToPdfAsync(Guid documentId);
    Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(Guid documentId, string? engineHint = null);
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

    public async Task<ExportDocument> BuildExportDocumentAsync(Guid documentId)
    {
        var doc = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc == null)
            throw new KeyNotFoundException($"Document {documentId} not found");

        var exportDoc = new ExportDocument
        {
            Title = doc.Title,
            Author = doc.OwnerId,
            Language = doc.Language,
            PaperSize = doc.PaperSize,
            FontFamily = doc.FontFamily,
            FontSize = doc.FontSize,
            Columns = doc.Columns,
            BalancedColumns = doc.BalancedColumns,
            Metadata = new ExportMetadata
            {
                Author = doc.OwnerId,
                Created = doc.CreatedAt,
                Modified = doc.UpdatedAt
            }
        };

        var theoremCounter = 0;

        foreach (var block in doc.Blocks)
        {
            if (block.Type.ToLowerInvariant() == "theorem")
                theoremCounter++;

            var exportBlock = await ConvertBlockAsync(block, theoremCounter);
            if (exportBlock != null)
                exportDoc.Blocks.Add(exportBlock);
        }

        if (doc.BibliographyEntries.Any())
        {
            exportDoc.Bibliography = doc.BibliographyEntries.Select(e => ConvertBibliographyEntry(e)).ToList();
        }

        return exportDoc;
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

    public async Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(Guid documentId, string? engineHint = null)
    {
        // engineHint = "typst"     → Typst only, throw if it fails
        // engineHint = "pdflatex"  → skip Typst, go straight to pdflatex
        // null / "auto" / anything else → existing behavior (Typst first,
        //                                  silent fallback to pdflatex)
        var hint = (engineHint ?? "auto").Trim().ToLowerInvariant();

        // Any explicit LaTeX engine means "compile this as LaTeX" — trying Typst
        // first would ignore the caller's request. Only "auto" (and "typst")
        // reach the Typst path.
        if (hint is not ("pdflatex" or "xelatex" or "lualatex"))
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
            IncludeImages = false,
            DocumentClass = "article",
            FontSize = "11pt",
            PaperSize = "a4paper",
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
        var engine = hint is "xelatex" or "lualatex" or "pdflatex"
            ? hint
            : await _context.Documents
                  .Where(d => d.Id == documentId)
                  .Select(d => d.LatexEngine)
                  .FirstOrDefaultAsync() ?? "pdflatex";

        var pdflatexPdf = await _latexRenderService.RenderToPdfTolerantAsync(
            latex, timeout: 60, engine: engine);
        return (pdflatexPdf, engine);
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
                && !entry.FullName.EndsWith(".bst")) continue;
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

    private async Task<ExportBlock?> ConvertBlockAsync(Block block, int theoremCounter)
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
                    RichText = ParseInlineFormatting(content.GetString() ?? "")
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
            "theorem" => ConvertTheoremBlock(content, theoremCounter),
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
                    Text = GetString(content, "title"),
                    Author = GetString(content, "author"),
                    DateText = GetString(content, "date"),
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
    private static List<ExportRichTextSpan> MapRichText(JsonElement content, string text)
    {
        if (content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("richText", out var stored)
            || stored.ValueKind != JsonValueKind.Array
            || stored.GetArrayLength() == 0)
        {
            return ParseInlineFormatting(text);
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

            foreach (var piece in ParseInlineFormatting(spanText))
            {
                spans.Add(WithMarks(marks, piece.Text, piece.Equation));
            }
        }

        return spans.Count > 0 ? spans : ParseInlineFormatting(text);
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

        return new ExportBlock
        {
            Type = "heading",
            Content = new ExportBlockContent
            {
                Text = text,
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
                Latex = latex,
                DisplayMode = displayMode
            }
        };
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
                Caption = GetString(content, "caption")
            }
        };
    }

    private ExportBlock ConvertListBlock(JsonElement content)
    {
        var listType = "bullet";
        if (content.TryGetProperty("listType", out var lt))
            listType = lt.GetString() ?? "bullet";
        else if (content.TryGetProperty("ordered", out var ord) && ord.ValueKind == JsonValueKind.True)
            listType = "ordered";

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

        var exportItem = new ExportListItem
        {
            Text = text,
            RichText = ParseInlineFormatting(text)
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
        var rows = new List<List<ExportTableCell>>();
        var hasHeader = GetBool(content, "hasHeader", true);

        if (content.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var exportRow = new List<ExportTableCell>();
                foreach (var cell in row.EnumerateArray())
                {
                    var cellText = cell.ValueKind == JsonValueKind.String
                        ? cell.GetString() ?? ""
                        : cell.ValueKind == JsonValueKind.Object
                            ? GetString(cell, "text")
                            : "";

                    exportRow.Add(new ExportTableCell
                    {
                        Text = cellText,
                        RichText = ParseInlineFormatting(cellText)
                    });
                }
                rows.Add(exportRow);
            }
        }

        return new ExportBlock
        {
            Type = "table",
            Content = new ExportBlockContent
            {
                Rows = rows,
                HasHeader = hasHeader
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

        if (imageData != null)
        {
            imageData.AltText = alt;
            imageData.Width = width * 500; // approximate pixel width
            imageData.Height = imageData.Width * 0.75; // default 4:3 aspect
        }

        return new ExportBlock
        {
            Type = "figure",
            Content = new ExportBlockContent
            {
                Image = imageData,
                Caption = caption
            }
        };
    }

    private ExportBlock ConvertBlockquoteBlock(JsonElement content)
    {
        var text = GetString(content, "text");
        return new ExportBlock
        {
            Type = "blockquote",
            Content = new ExportBlockContent
            {
                Text = text,
                RichText = ParseInlineFormatting(text)
            }
        };
    }

    private ExportBlock ConvertTheoremBlock(JsonElement content, int theoremCounter)
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
                TheoremNumber = theoremCounter,
                Text = title,  // title goes in Text for the header
                RichText = ParseInlineFormatting(text) // body text in RichText
            }
        };
    }

    private ExportBlock ConvertAbstractBlock(JsonElement content)
    {
        var text = GetString(content, "text");
        return new ExportBlock
        {
            Type = "abstract",
            Content = new ExportBlockContent
            {
                Text = text,
                RichText = ParseInlineFormatting(text)
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
        var code = GetString(content, "code");
        if (string.IsNullOrEmpty(code)) code = GetString(content, "text");
        if (string.IsNullOrEmpty(code)) code = GetString(content, "content");
        var caption = GetString(content, "caption");

        if (string.IsNullOrEmpty(caption) && !string.IsNullOrEmpty(title))
            caption = $"Algorithm: {title}";

        return new ExportBlock
        {
            Type = "algorithm",
            Content = new ExportBlockContent
            {
                Code = code,
                Caption = caption,
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
                Text = title,
                RichText = ParseInlineFormatting(text)
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
                RichText = ParseInlineFormatting(text)
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
            Fields = fields
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
    public static List<ExportRichTextSpan> ParseInlineFormatting(string? text)
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
