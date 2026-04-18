using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

public interface IDocumentExportService
{
    Task<ExportDocument> BuildExportDocumentAsync(Guid documentId);
    Task<byte[]> ExportToDocxAsync(Guid documentId);
    Task<byte[]> ExportToPdfAsync(Guid documentId);
}

public class DocumentExportService : IDocumentExportService
{
    private readonly LiliaDbContext _context;
    private readonly IDocxExportService _docxExportService;
    private readonly IRenderService _renderService;
    private readonly ILaTeXExportService _latexExportService;
    private readonly ILaTeXRenderService _latexRenderService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DocumentExportService> _logger;

    public DocumentExportService(
        LiliaDbContext context,
        IDocxExportService docxExportService,
        IRenderService renderService,
        ILaTeXExportService latexExportService,
        ILaTeXRenderService latexRenderService,
        IHttpClientFactory httpClientFactory,
        ILogger<DocumentExportService> logger)
    {
        _context = context;
        _docxExportService = docxExportService;
        _renderService = renderService;
        _latexExportService = latexExportService;
        _latexRenderService = latexRenderService;
        _httpClientFactory = httpClientFactory;
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
        // Go through LaTeXExportService so PDF uses the same preamble builder
        // as the ZIP export — single source of truth for class/options/packages
        // filtering and the journal shims.
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
        // Tolerant mode — body errors produce a partial PDF instead of 500.
        // Preamble errors still surface (no PDF file generated → exception).
        return await _latexRenderService.RenderToPdfTolerantAsync(latex, timeout: 60);
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
            "figure" => await ConvertFigureBlockAsync(content),
            "blockquote" => ConvertBlockquoteBlock(content),
            "theorem" => ConvertTheoremBlock(content, theoremCounter),
            "abstract" => ConvertAbstractBlock(content),
            "pagebreak" => ConvertPageBreakBlock(),
            "tableofcontents" => ConvertTableOfContentsBlock(),
            "algorithm" => ConvertAlgorithmBlock(content),
            "callout" => ConvertCalloutBlock(content),
            "footnote" => ConvertFootnoteBlock(content),
            "bibliography" => null, // handled at document level
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
                RichText = ParseInlineFormatting(text)
            }
        };
    }

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
        var latex = GetString(content, "latex");
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

    private async Task<ExportBlock> ConvertFigureBlockAsync(JsonElement content)
    {
        var src = GetString(content, "src");
        var alt = GetString(content, "alt");
        var caption = GetString(content, "caption");
        var width = content.TryGetProperty("width", out var w) ? w.GetDouble() : 0.8;

        ExportImageData? imageData = null;
        if (!string.IsNullOrEmpty(src))
        {
            imageData = await DownloadImageAsync(src);
            if (imageData != null)
            {
                imageData.AltText = alt;
                imageData.Width = width * 500; // approximate pixel width
                imageData.Height = imageData.Width * 0.75; // default 4:3 aspect
            }
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
        var code = GetString(content, "code");
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

    private async Task<ExportImageData?> DownloadImageAsync(string src)
    {
        try
        {
            if (!Uri.TryCreate(src, UriKind.Absolute, out var uri))
                return null;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";

            return new ExportImageData
            {
                Data = Convert.ToBase64String(bytes),
                MimeType = contentType,
                Filename = Path.GetFileName(uri.LocalPath)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Src}", src);
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
