using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

public partial class RenderService : IRenderService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<RenderService> _logger;
    private const int BlocksPerPage = 15;

    public RenderService(LiliaDbContext context, ILogger<RenderService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> GetPageCountAsync(Guid documentId)
    {
        var blockCount = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .CountAsync();

        return Math.Max(1, (int)Math.Ceiling((double)blockCount / BlocksPerPage));
    }

    public async Task<List<SectionDto>> GetSectionsAsync(Guid documentId)
    {
        var allBlocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var headings = allBlocks.Where(b => b.Type == "heading").ToList();

        return headings.Select(h =>
        {
            var blockIndex = allBlocks.FindIndex(b => b.Id == h.Id);
            var startPage = (blockIndex / BlocksPerPage) + 1;

            var title = "Untitled";
            var level = 1;

            try
            {
                if (h.Content.RootElement.TryGetProperty("text", out var textProp))
                {
                    title = textProp.GetString() ?? "Untitled";
                }
                if (h.Content.RootElement.TryGetProperty("level", out var levelProp))
                {
                    level = levelProp.GetInt32();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse heading content for block {BlockId}", h.Id);
            }

            return new SectionDto(
                h.Id.ToString(),
                title,
                level,
                startPage,
                startPage + 1
            );
        }).ToList();
    }

    public async Task<string> RenderPageAsync(Guid documentId, int page)
    {
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .Skip((page - 1) * BlocksPerPage)
            .Take(BlocksPerPage)
            .ToListAsync();

        _logger.LogDebug("[Render] Page {Page} has {Count} blocks: [{Types}]",
            page, blocks.Count, string.Join(", ", blocks.Select(b => b.Type)));

        // Check if any block is a bibliography block - if so, fetch entries (case-insensitive)
        var hasBibliography = blocks.Any(b => string.Equals(b.Type, "bibliography", StringComparison.OrdinalIgnoreCase));
        IEnumerable<BibliographyEntry>? bibEntries = null;
        if (hasBibliography)
        {
            bibEntries = await _context.BibliographyEntries
                .Where(e => e.DocumentId == documentId)
                .OrderBy(e => e.CiteKey)
                .ToListAsync();
            _logger.LogDebug("[Render] Found {Count} bibliography entries for document {DocId}",
                bibEntries.Count(), documentId);
        }

        var html = new StringBuilder();
        html.Append($"<div class=\"lilia-page\" data-page=\"{page}\">");

        foreach (var block in blocks)
        {
            html.Append($"<div data-block-id=\"{block.Id}\" class=\"studio-block\" data-block-type=\"{block.Type}\">");
            if (string.Equals(block.Type, "bibliography", StringComparison.OrdinalIgnoreCase) && bibEntries != null)
            {
                _logger.LogDebug("[Render] Rendering bibliography block with {Count} entries", bibEntries.Count());
                html.Append(RenderBibliographyEntriesHtml(bibEntries));
            }
            else
            {
                html.Append(RenderBlockToHtml(block));
            }
            html.Append("</div>");
        }

        html.Append("</div>");
        return html.ToString();
    }

    public async Task<string> RenderToHtmlAsync(Guid documentId)
    {
        var doc = await _context.Documents.FindAsync(documentId);
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        // Check if any block is a bibliography block - if so, fetch entries (case-insensitive)
        var hasBibliography = blocks.Any(b => string.Equals(b.Type, "bibliography", StringComparison.OrdinalIgnoreCase));
        IEnumerable<BibliographyEntry>? bibEntries = null;
        if (hasBibliography)
        {
            bibEntries = await _context.BibliographyEntries
                .Where(e => e.DocumentId == documentId)
                .OrderBy(e => e.CiteKey)
                .ToListAsync();
        }

        var html = new StringBuilder();

        // Apply multi-column CSS if document has columns > 1
        if (doc != null && doc.Columns > 1)
        {
            html.Append($"<div class=\"lilia-preview\" style=\"column-count: {doc.Columns}; column-gap: {doc.ColumnGap}cm;");
            if (doc.ColumnSeparator == "rule")
            {
                html.Append(" column-rule: 1px solid #ccc;");
            }
            html.Append("\">");
        }
        else
        {
            html.Append("<div class=\"lilia-preview\">");
        }

        foreach (var block in blocks)
        {
            html.Append($"<div data-block-id=\"{block.Id}\" class=\"studio-block\" data-block-type=\"{block.Type}\">");
            if (string.Equals(block.Type, "bibliography", StringComparison.OrdinalIgnoreCase) && bibEntries != null)
            {
                html.Append(RenderBibliographyEntriesHtml(bibEntries));
            }
            else
            {
                html.Append(RenderBlockToHtml(block));
            }
            html.Append("</div>");
        }

        html.Append("</div>");
        return html.ToString();
    }

    public string RenderBlockToHtml(Block block)
    {
        try
        {
            var content = block.Content.RootElement;

            return block.Type.ToLowerInvariant() switch
            {
                "heading" or "header" => RenderHeadingToHtml(content),
                "paragraph" => RenderParagraphToHtml(content),
                "equation" => RenderEquationToHtml(content),
                "figure" or "image" => RenderFigureToHtml(content),
                "table" => RenderTableToHtml(content),
                "code" => RenderCodeToHtml(content),
                "list" => RenderListToHtml(content),
                "blockquote" or "quote" => RenderBlockquoteToHtml(content),
                "theorem" => RenderTheoremToHtml(content),
                "abstract" => RenderAbstractToHtml(content),
                "tableofcontents" => "<div class=\"table-of-contents\"><h3>Table of Contents</h3><p>Auto-generated from headings</p></div>",
                "bibliography" => RenderBibliographyToHtml(content),
                "columnbreak" => "<div class=\"column-break\"><span class=\"column-break-label\">Column Break</span></div>",
                "pagebreak" or "divider" => "<div class=\"page-break\"><span class=\"page-break-label\">Page Break</span></div>",
                "embed" => RenderEmbedToHtml(content),
                "algorithm" => RenderAlgorithmToHtml(content),
                "callout" => RenderCalloutToHtml(content),
                "footnote" => RenderFootnoteToHtml(content),
                _ => $"<div class=\"block block-{WebUtility.HtmlEncode(block.Type)}\"></div>"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} of type {BlockType}", block.Id, block.Type);
            return $"<div class=\"block block-error\">Error rendering block</div>";
        }
    }

    private string RenderHeadingToHtml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var level = content.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
        level = Math.Clamp(level, 1, 6);
        var escaped = WebUtility.HtmlEncode(text);
        return $"<h{level} class=\"heading\">{escaped}</h{level}>";
    }

    private string RenderParagraphToHtml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var processed = ProcessInlineContent(text);
        return $"<p class=\"paragraph\">{processed}</p>";
    }

    private string RenderEquationToHtml(JsonElement content)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";

        // Check both "equationMode" string and legacy "displayMode" boolean
        var equationMode = content.TryGetProperty("equationMode", out var em) ? em.GetString() ?? "display" : "display";
        var displayMode = equationMode != "inline"
            || (content.TryGetProperty("displayMode", out var d) && d.ValueKind == JsonValueKind.True);

        // Strip MathLive placeholder artifacts
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");

        var escaped = WebUtility.HtmlEncode(latex);

        // Auto-detect paragraph-level math environments
        var containsParagraphEnv = ParagraphMathEnvironments.Any(env =>
            latex.Contains($"\\begin{{{env}}}"));

        if (displayMode || containsParagraphEnv)
        {
            return $"<div class=\"equation display-math\" data-latex=\"{escaped}\">$${escaped}$$</div>";
        }
        return $"<span class=\"inline-math\" data-latex=\"{escaped}\">${escaped}$</span>";
    }

    private string RenderFigureToHtml(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var alt = content.TryGetProperty("alt", out var a) ? a.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var width = content.TryGetProperty("width", out var w) ? w.GetDouble() : 0.8;
        var position = content.TryGetProperty("position", out var p) ? p.GetString() ?? "center" : "center";
        var placement = content.TryGetProperty("placement", out var pl) ? pl.GetString() ?? "auto" : "auto";

        var srcEscaped = WebUtility.HtmlEncode(src);
        var altEscaped = WebUtility.HtmlEncode(alt);
        var captionEscaped = WebUtility.HtmlEncode(caption);

        var widthPercent = (int)(width * 100);
        var textAlign = position switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center"
        };

        var html = new StringBuilder();

        // Check for subfigures
        if (content.TryGetProperty("subfigures", out var subfigures) && subfigures.ValueKind == JsonValueKind.Array && subfigures.GetArrayLength() > 0)
        {
            html.Append($"<figure class=\"figure figure-placement-{WebUtility.HtmlEncode(placement)}\" style=\"width: {widthPercent}%; text-align: {textAlign}\">");
            html.Append("<div class=\"subfigures\" style=\"display: flex; gap: 1em; justify-content: center;\">");
            foreach (var subfig in subfigures.EnumerateArray())
            {
                var subSrc = subfig.TryGetProperty("src", out var ss) ? ss.GetString() ?? "" : "";
                var subAlt = subfig.TryGetProperty("alt", out var sa) ? sa.GetString() ?? "" : "";
                var subCaption = subfig.TryGetProperty("caption", out var sc) ? sc.GetString() ?? "" : "";
                html.Append("<figure class=\"subfigure\" style=\"flex: 1;\">");
                html.Append($"<img src=\"{WebUtility.HtmlEncode(subSrc)}\" alt=\"{WebUtility.HtmlEncode(subAlt)}\" />");
                if (!string.IsNullOrEmpty(subCaption))
                    html.Append($"<figcaption>{WebUtility.HtmlEncode(subCaption)}</figcaption>");
                html.Append("</figure>");
            }
            html.Append("</div>");
            if (!string.IsNullOrEmpty(caption))
                html.Append($"<figcaption>{captionEscaped}</figcaption>");
            html.Append("</figure>");
        }
        else
        {
            html.Append($"<figure class=\"figure figure-placement-{WebUtility.HtmlEncode(placement)}\" style=\"width: {widthPercent}%; text-align: {textAlign}\">");
            html.Append($"<img src=\"{srcEscaped}\" alt=\"{altEscaped}\" />");
            if (!string.IsNullOrEmpty(caption))
            {
                html.Append($"<figcaption>{captionEscaped}</figcaption>");
            }
            html.Append("</figure>");
        }

        return html.ToString();
    }

    private string RenderTableToHtml(JsonElement content)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"table-container\"><table class=\"table\">");

        // Table caption
        if (content.TryGetProperty("caption", out var captionProp) && captionProp.ValueKind == JsonValueKind.String)
        {
            var captionText = captionProp.GetString() ?? "";
            if (!string.IsNullOrEmpty(captionText))
                html.Append($"<caption>{WebUtility.HtmlEncode(captionText)}</caption>");
        }

        // Read column alignments
        string[] colAlignments = [];
        if (content.TryGetProperty("columnAlign", out var alignProp) && alignProp.ValueKind == JsonValueKind.Array)
        {
            colAlignments = alignProp.EnumerateArray()
                .Select(a => a.GetString()?.ToLowerInvariant() ?? "l")
                .ToArray();
        }

        if (content.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var isFirst = true;
            foreach (var row in rows.EnumerateArray())
            {
                html.Append("<tr>");
                if (row.ValueKind == JsonValueKind.Array)
                {
                    var colIdx = 0;
                    foreach (var cell in row.EnumerateArray())
                    {
                        var cellText = GetCellText(cell);
                        var escaped = WebUtility.HtmlEncode(cellText);
                        var tag = isFirst ? "th" : "td";

                        var attrs = new StringBuilder();

                        // Column alignment
                        if (colIdx < colAlignments.Length)
                        {
                            var textAlign = colAlignments[colIdx] switch
                            {
                                "c" => "center",
                                "r" => "right",
                                _ => (string?)null
                            };
                            if (textAlign != null)
                                attrs.Append($" style=\"text-align: {textAlign}\"");
                        }

                        // Colspan
                        var colspan = GetCellIntProp(cell, "colspan", 1);
                        if (colspan > 1)
                            attrs.Append($" colspan=\"{colspan}\"");

                        // Rowspan
                        var rowspan = GetCellIntProp(cell, "rowspan", 1);
                        if (rowspan > 1)
                            attrs.Append($" rowspan=\"{rowspan}\"");

                        html.Append($"<{tag}{attrs}>{escaped}</{tag}>");
                        colIdx += Math.Max(colspan, 1);
                    }
                }
                html.Append("</tr>");
                isFirst = false;
            }
        }

        html.Append("</table></div>");
        return html.ToString();
    }

    private string RenderCodeToHtml(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var language = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var lineNumbers = content.TryGetProperty("lineNumbers", out var ln) && ln.ValueKind == JsonValueKind.True;
        var escaped = WebUtility.HtmlEncode(code);
        var langAttr = !string.IsNullOrEmpty(language) ? $" data-language=\"{WebUtility.HtmlEncode(language)}\"" : "";
        var lineNumClass = lineNumbers ? " line-numbers" : "";

        var html = new StringBuilder();
        html.Append($"<div class=\"code-block{lineNumClass}\"{langAttr}>");
        html.Append($"<code>{escaped}</code>");
        if (!string.IsNullOrEmpty(caption))
        {
            html.Append($"<figcaption>{WebUtility.HtmlEncode(caption)}</figcaption>");
        }
        html.Append("</div>");
        return html.ToString();
    }

    private string RenderListToHtml(JsonElement content)
    {
        // Support both "listType" (string: "ordered"/"unordered") and "ordered" (boolean) formats
        var isOrdered = false;
        if (content.TryGetProperty("listType", out var lt))
        {
            isOrdered = lt.GetString() == "ordered";
        }
        else if (content.TryGetProperty("ordered", out var ord))
        {
            isOrdered = ord.ValueKind == JsonValueKind.True;
        }

        var tag = isOrdered ? "ol" : "ul";

        var html = new StringBuilder();

        // Build attributes for the list element
        var attrs = new StringBuilder();
        attrs.Append($" class=\"list\"");

        if (isOrdered)
        {
            // Label format → HTML type attribute
            if (content.TryGetProperty("labelFormat", out var lfProp))
            {
                var labelFormat = lfProp.GetString() ?? "number";
                var typeAttr = labelFormat switch
                {
                    "alpha" => "a",
                    "Alpha" => "A",
                    "roman" => "i",
                    "Roman" => "I",
                    _ => (string?)null // "number" is default
                };
                if (typeAttr != null)
                    attrs.Append($" type=\"{typeAttr}\"");
            }

            // Support start number for ordered lists
            if (content.TryGetProperty("start", out var startProp) && startProp.TryGetInt32(out var startNum) && startNum != 1)
            {
                attrs.Append($" start=\"{startNum}\"");
            }
        }

        html.Append($"<{tag}{attrs}>");

        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                RenderListItemToHtml(item, isOrdered, html);
            }
        }

        html.Append($"</{tag}>");
        return html.ToString();
    }

    private void RenderListItemToHtml(JsonElement item, bool parentIsOrdered, StringBuilder html)
    {
        // Support both string items and object items with text/richText/children properties
        string itemText;
        JsonElement? children = null;

        if (item.ValueKind == JsonValueKind.String)
        {
            itemText = item.GetString() ?? "";
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            // Handle object format: { text?: string, richText?: Array<{text: string}>, children?: [...] }
            if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                itemText = textProp.GetString() ?? "";
            }
            else if (item.TryGetProperty("richText", out var richTextProp) && richTextProp.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var span in richTextProp.EnumerateArray())
                {
                    if (span.TryGetProperty("text", out var spanText) && spanText.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(spanText.GetString() ?? "");
                    }
                }
                itemText = sb.ToString();
            }
            else
            {
                itemText = "";
            }

            // Check for nested children
            if (item.TryGetProperty("children", out var childrenProp) && childrenProp.ValueKind == JsonValueKind.Array && childrenProp.GetArrayLength() > 0)
            {
                children = childrenProp;
            }
        }
        else
        {
            itemText = "";
        }

        var escaped = ProcessInlineContent(itemText);
        html.Append($"<li>{escaped}");

        // Render nested list if children present
        if (children.HasValue)
        {
            var nestedTag = parentIsOrdered ? "ol" : "ul";
            html.Append($"<{nestedTag}>");
            foreach (var child in children.Value.EnumerateArray())
            {
                RenderListItemToHtml(child, parentIsOrdered, html);
            }
            html.Append($"</{nestedTag}>");
        }

        html.Append("</li>");
    }

    private string RenderAbstractToHtml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var processed = ProcessInlineContent(text);
        return $"<div class=\"abstract\"><h3>Abstract</h3><p>{processed}</p></div>";
    }

    private string RenderBlockquoteToHtml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var processed = ProcessInlineContent(text);
        return $"<blockquote class=\"blockquote\">{processed}</blockquote>";
    }

    private string RenderTheoremToHtml(JsonElement content)
    {
        var theoremType = content.TryGetProperty("theoremType", out var tt) ? tt.GetString() ?? "theorem" : "theorem";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var numbered = !content.TryGetProperty("numbered", out var numProp) || numProp.ValueKind != JsonValueKind.False;
        var label = content.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "" : "";

        var typeEscaped = WebUtility.HtmlEncode(theoremType);
        var titleEscaped = WebUtility.HtmlEncode(title);
        var processed = ProcessInlineContent(text);

        var isProof = theoremType == "proof";
        var bodyClass = isProof ? "theorem-body" : "theorem-body italic";

        var html = new StringBuilder();
        var numberedClass = numbered ? "" : " unnumbered";
        html.Append($"<div class=\"theorem theorem-{typeEscaped}{numberedClass}\"");
        if (!string.IsNullOrEmpty(label))
        {
            html.Append($" id=\"{WebUtility.HtmlEncode(label)}\"");
        }
        html.Append(">");
        html.Append($"<div class=\"theorem-header\"><strong>{char.ToUpper(typeEscaped[0])}{typeEscaped[1..]}</strong>");
        if (!string.IsNullOrEmpty(title))
        {
            html.Append($" ({titleEscaped})");
        }
        html.Append(".</div>");
        html.Append($"<div class=\"{bodyClass}\">{processed}</div>");
        if (isProof)
        {
            html.Append("<div class=\"qed\">&#9633;</div>");
        }
        html.Append("</div>");
        return html.ToString();
    }

    private string RenderBibliographyToHtml(JsonElement content)
    {
        // This is called when we don't have entries - just return placeholder
        return "<div class=\"bibliography\"><h3>References</h3><div class=\"bibliography-entries\"></div></div>";
    }

    private string RenderEmbedToHtml(JsonElement content)
    {
        var engine = content.TryGetProperty("engine", out var e) ? e.GetString() ?? "latex" : "latex";
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var escaped = WebUtility.HtmlEncode(code);
        var html = $"<div class=\"embed embed-{WebUtility.HtmlEncode(engine)}\">";
        html += $"<pre><code>{escaped}</code></pre>";
        if (!string.IsNullOrEmpty(caption))
            html += $"<p class=\"embed-caption\">{WebUtility.HtmlEncode(caption)}</p>";
        html += "</div>";
        return html;
    }

    private string RenderAlgorithmToHtml(JsonElement content)
    {
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var html = "<div class=\"algorithm\">";
        if (!string.IsNullOrEmpty(title))
            html += $"<h4 class=\"algorithm-title\">{WebUtility.HtmlEncode(title)}</h4>";
        html += $"<pre class=\"algorithm-code\"><code>{WebUtility.HtmlEncode(code)}</code></pre>";
        if (!string.IsNullOrEmpty(caption))
            html += $"<p class=\"algorithm-caption\">{WebUtility.HtmlEncode(caption)}</p>";
        html += "</div>";
        return html;
    }

    private string RenderCalloutToHtml(JsonElement content)
    {
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var html = $"<div class=\"callout callout-{WebUtility.HtmlEncode(variant)}\">";
        if (!string.IsNullOrEmpty(title))
            html += $"<p class=\"callout-title\">{WebUtility.HtmlEncode(title)}</p>";
        html += $"<p class=\"callout-text\">{ProcessInlineContent(text)}</p>";
        html += "</div>";
        return html;
    }

    private string RenderFootnoteToHtml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"<span class=\"footnote\">{ProcessInlineContent(text)}</span>";
    }

    private string RenderBibliographyEntriesHtml(IEnumerable<BibliographyEntry> entries)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"bibliography\">");
        html.Append("<h3>References</h3>");
        html.Append("<div class=\"bibliography-entries\">");

        foreach (var entry in entries)
        {
            html.Append($"<div class=\"bib-entry\" data-cite-key=\"{WebUtility.HtmlEncode(entry.CiteKey)}\">");
            html.Append($"<span class=\"bib-key\">[{WebUtility.HtmlEncode(entry.CiteKey)}]</span> ");

            // Build citation from entry data
            try
            {
                var data = entry.Data?.RootElement;
                if (data.HasValue)
                {
                    var parts = new List<string>();

                    if (data.Value.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.String)
                    {
                        var authorStr = author.GetString();
                        if (!string.IsNullOrEmpty(authorStr))
                            parts.Add(WebUtility.HtmlEncode(authorStr));
                    }

                    if (data.Value.TryGetProperty("year", out var year) && year.ValueKind == JsonValueKind.String)
                    {
                        var yearStr = year.GetString();
                        if (!string.IsNullOrEmpty(yearStr))
                            parts.Add($"({WebUtility.HtmlEncode(yearStr)})");
                    }

                    if (data.Value.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    {
                        var titleStr = title.GetString();
                        if (!string.IsNullOrEmpty(titleStr))
                            parts.Add($"<em>{WebUtility.HtmlEncode(titleStr)}</em>");
                    }

                    if (data.Value.TryGetProperty("journal", out var journal) && journal.ValueKind == JsonValueKind.String)
                    {
                        var journalStr = journal.GetString();
                        if (!string.IsNullOrEmpty(journalStr))
                            parts.Add(WebUtility.HtmlEncode(journalStr));
                    }

                    if (data.Value.TryGetProperty("publisher", out var publisher) && publisher.ValueKind == JsonValueKind.String)
                    {
                        var publisherStr = publisher.GetString();
                        if (!string.IsNullOrEmpty(publisherStr))
                            parts.Add(WebUtility.HtmlEncode(publisherStr));
                    }

                    html.Append(string.Join(". ", parts));
                    if (parts.Count > 0) html.Append(".");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse bibliography entry {CiteKey}", entry.CiteKey);
                html.Append(WebUtility.HtmlEncode(entry.CiteKey));
            }

            html.Append("</div>");
        }

        html.Append("</div></div>");
        return html.ToString();
    }

    private string ProcessInlineContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // First escape HTML
        var result = WebUtility.HtmlEncode(text);

        // Display math: $$...$$
        result = Regex.Replace(result, @"\$\$(.+?)\$\$",
            m => $"<div class=\"display-math\" data-latex=\"{m.Groups[1].Value}\">$${m.Groups[1].Value}$$</div>");

        // Inline math: $...$ (but not $$...$$)
        result = Regex.Replace(result, @"(?<!\$)\$(?!\$)(.+?)(?<!\$)\$(?!\$)",
            m => $"<span class=\"inline-math\" data-latex=\"{m.Groups[1].Value}\">${m.Groups[1].Value}$</span>");

        // Bold: **...**
        result = Regex.Replace(result, @"\*\*(.+?)\*\*",
            m => $"<strong>{m.Groups[1].Value}</strong>");

        // Italic: *...*
        result = Regex.Replace(result, @"\*(.+?)\*",
            m => $"<em>{m.Groups[1].Value}</em>");

        // Underline: __...__
        result = Regex.Replace(result, @"__(.+?)__",
            m => $"<u>{m.Groups[1].Value}</u>");

        // Strikethrough: ~~...~~
        result = Regex.Replace(result, @"~~(.+?)~~",
            m => $"<del>{m.Groups[1].Value}</del>");

        // Inline code: `...`
        result = Regex.Replace(result, @"`(.+?)`",
            m => $"<code>{m.Groups[1].Value}</code>");

        // Citations: \cite{{...}}
        result = Regex.Replace(result, @"\\cite\{(.+?)\}",
            m => $"<cite data-cite=\"{m.Groups[1].Value}\">[{m.Groups[1].Value}]</cite>");

        // References: \ref{{...}}, \cref{{...}}
        result = Regex.Replace(result, @"\\(?:c?ref|Cref|eqref)\{(.+?)\}",
            m => $"<a class=\"ref\" data-ref=\"{m.Groups[1].Value}\">{m.Groups[1].Value}</a>");

        // URLs: \url{{...}}
        result = Regex.Replace(result, @"\\url\{(.+?)\}",
            m => $"<a href=\"{m.Groups[1].Value}\" class=\"url\" target=\"_blank\" rel=\"noopener\">{m.Groups[1].Value}</a>");

        // Hyperlinks: \href{{url}}{{text}}
        result = Regex.Replace(result, @"\\href\{(.+?)\}\{(.+?)\}",
            m => $"<a href=\"{m.Groups[1].Value}\" target=\"_blank\" rel=\"noopener\">{m.Groups[2].Value}</a>");

        return result;
    }

    // Mirror of LaTeXExportService.BuildDocumentClassDirective but reads only
    // from the Document — no LaTeXExportOptions available in this code path.
    private static readonly HashSet<string> SafeDocumentClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "article", "report", "book", "letter", "minimal",
        "amsart", "amsbook", "amsproc",
        "memoir",
        "scrartcl", "scrbook", "scrreprt"
    };

    private static readonly HashSet<string> DefaultPreamblePackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "inputenc", "fontenc", "textcomp", "lmodern",
        "amsmath", "amssymb", "amsfonts", "amsthm", "mathtools", "mathrsfs", "cancel", "siunitx",
        "microtype", "setspace", "parskip",
        "graphicx", "float", "caption", "subcaption", "xcolor",
        "booktabs", "multirow", "tabularx", "longtable", "array",
        "enumitem", "listings",
        "algorithm", "algorithmic",
        "tcolorbox", "hyperref", "cleveref", "csquotes",
        "geometry", "babel",
        // Typeface / math-font packages that conflict with amsmath (\iint etc.).
        "newtxtext", "newtxmath", "mathptmx", "txfonts", "pxfonts",
        "mathpazo", "fourier", "libertine", "palatino", "utopia",
        "charter", "cmbright", "kpfonts", "eulervm"
    };

    private static string BuildDocumentClassDirectiveFromDoc(Document doc)
    {
        var stored = doc.LatexDocumentClass?.Trim();
        var className = !string.IsNullOrWhiteSpace(stored) && SafeDocumentClasses.Contains(stored) ? stored : "article";
        var opts = new List<string>();
        opts.Add($"{doc.FontSize}pt");
        opts.Add(string.Equals(doc.PaperSize, "letter", StringComparison.OrdinalIgnoreCase) ? "letterpaper" : "a4paper");
        if (!string.IsNullOrWhiteSpace(doc.LatexDocumentClassOptions))
        {
            foreach (var t in doc.LatexDocumentClassOptions.Split(','))
            {
                var o = t.Trim();
                if (!string.IsNullOrEmpty(o) && !opts.Contains(o)) opts.Add(o);
            }
        }
        if (doc.Columns >= 2 && !opts.Any(o => string.Equals(o, "twocolumn", StringComparison.OrdinalIgnoreCase)))
        {
            opts.Add("twocolumn");
        }
        return opts.Count > 0
            ? $"\\documentclass[{string.Join(",", opts)}]{{{className}}}"
            : $"\\documentclass{{{className}}}";
    }

    private static string BuildImportedPackageLinesFromDoc(Document doc)
    {
        if (string.IsNullOrWhiteSpace(doc.LatexPackages)) return string.Empty;
        try
        {
            using var json = JsonDocument.Parse(doc.LatexPackages);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;
            var sb = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            sb.AppendLine("% Packages preserved from imported preamble (IfFileExists-wrapped)");
            foreach (var pkg in json.RootElement.EnumerateArray())
            {
                var name = pkg.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (DefaultPreamblePackages.Contains(name)) continue;
                if (!emitted.Add(name)) continue;
                var o = pkg.TryGetProperty("options", out var opel) ? opel.GetString() : null;
                var load = string.IsNullOrWhiteSpace(o)
                    ? $"\\usepackage{{{name}}}"
                    : $"\\usepackage[{o}]{{{name}}}";
                sb.AppendLine($"\\IfFileExists{{{name}.sty}}{{{load}}}{{}}");
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    // LaTeX rendering
    public async Task<string> RenderToLatexAsync(Guid documentId)
    {
        var doc = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc == null)
        {
            throw new ArgumentException("Document not found");
        }

        var latex = new StringBuilder();

        // Preamble — honour imported class / options / packages so PDFs compile
        // against mnras/pnas/frontiers templates instead of bare article.
        latex.AppendLine(BuildDocumentClassDirectiveFromDoc(doc));
        // Imported packages first so journal-class dependencies load before our defaults.
        var importedPkgs = BuildImportedPackageLinesFromDoc(doc);
        if (!string.IsNullOrEmpty(importedPkgs))
        {
            latex.AppendLine(importedPkgs);
        }
        latex.AppendLine(LaTeXPreamble.Packages);
        // Journal-class shims — safe no-ops when commands already exist
        latex.AppendLine(LaTeXPreamble.JournalShims);

        // Multi-column support
        if (doc.Columns > 1)
        {
            latex.AppendLine(@"\usepackage{multicol}");
            latex.AppendLine($@"\setlength{{\columnsep}}{{{doc.ColumnGap}cm}}");
            if (doc.ColumnSeparator == "rule")
            {
                latex.AppendLine(@"\setlength{\columnseprule}{0.4pt}");
            }
        }

        latex.AppendLine();

        // Theorem environments
        latex.AppendLine(LaTeXPreamble.TheoremEnvironments);

        latex.AppendLine($@"\title{{{EscapeLatex(doc.Title)}}}");
        latex.AppendLine(@"\begin{document}");
        latex.AppendLine(@"\maketitle");
        latex.AppendLine();

        // Multi-column wrapper
        if (doc.Columns > 1)
        {
            latex.AppendLine($@"\begin{{multicols}}{{{doc.Columns}}}");
            latex.AppendLine();
        }

        // Blocks
        foreach (var block in doc.Blocks)
        {
            latex.AppendLine($"% block:{block.Id}");
            latex.AppendLine(RenderBlockToLatex(block));
        }

        // Close multi-column wrapper
        if (doc.Columns > 1)
        {
            latex.AppendLine();
            latex.AppendLine(@"\end{multicols}");
        }

        // Bibliography
        if (doc.BibliographyEntries != null && doc.BibliographyEntries.Any())
        {
            latex.AppendLine();
            latex.AppendLine(RenderBibliographyToLatex(doc.BibliographyEntries));
        }

        latex.AppendLine(@"\end{document}");
        return latex.ToString();
    }

    public string RenderBlockToLatex(Block block)
    {
        try
        {
            var content = block.Content.RootElement;

            return block.Type.ToLowerInvariant() switch
            {
                "heading" or "header" => RenderHeadingToLatex(content),
                "paragraph" => RenderParagraphToLatex(content),
                "equation" => RenderEquationToLatex(content),
                "figure" or "image" => RenderFigureToLatex(content),
                "table" => RenderTableToLatex(content),
                "code" => RenderCodeToLatex(content),
                "list" => RenderListToLatex(content),
                "blockquote" or "quote" => RenderBlockquoteToLatex(content),
                "theorem" => RenderTheoremToLatex(content),
                "abstract" => RenderAbstractToLatex(content),
                "tableofcontents" => @"\tableofcontents",
                "columnbreak" => @"\columnbreak",
                "pagebreak" or "divider" => @"\newpage",
                "bibliography" => "", // handled separately via BibliographyEntries
                "embed" => RenderEmbedToLatex(content),
                "algorithm" => RenderAlgorithmToLatex(content),
                "callout" => RenderCalloutToLatex(content),
                "footnote" => RenderFootnoteToLatex(content),
                _ => $"% Unknown block type: {block.Type}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to LaTeX", block.Id);
            return $"% Error rendering block: {block.Id}";
        }
    }

    private string RenderHeadingToLatex(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var level = content.TryGetProperty("level", out var l) ? l.GetInt32() : 1;

        var command = level switch
        {
            1 => "section",
            2 => "subsection",
            3 => "subsubsection",
            4 => "paragraph",
            5 => "subparagraph",
            _ => "section"
        };

        return $@"\{command}{{{EscapeLatex(text)}}}";
    }

    private string RenderParagraphToLatex(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return ProcessLatexText(text) + "\n";
    }

    private string RenderAbstractToLatex(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $@"\begin{{abstract}}
{ProcessLatexText(text)}
\end{{abstract}}";
    }

    private string RenderEmbedToLatex(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        // Embed blocks are raw code escape hatches — output the code directly
        return code;
    }

    private string RenderAlgorithmToLatex(JsonElement content)
    {
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{algorithm}[H]");
        var displayCaption = !string.IsNullOrEmpty(caption) ? caption : title;
        if (!string.IsNullOrEmpty(displayCaption))
            sb.AppendLine($@"\caption{{{EscapeLatex(displayCaption)}}}");
        if (!string.IsNullOrEmpty(label))
            sb.AppendLine($@"\label{{{label}}}");
        sb.AppendLine(@"\begin{algorithmic}");
        // Wrap bare pseudocode lines in \STATE if they don't start with algorithmic commands
        foreach (var line in code.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                sb.AppendLine();
                continue;
            }
            // Check if line already starts with an algorithmic command
            if (trimmed.StartsWith("\\STATE") || trimmed.StartsWith("\\IF") ||
                trimmed.StartsWith("\\ELSE") || trimmed.StartsWith("\\ELSIF") ||
                trimmed.StartsWith("\\ENDIF") || trimmed.StartsWith("\\FOR") ||
                trimmed.StartsWith("\\ENDFOR") || trimmed.StartsWith("\\WHILE") ||
                trimmed.StartsWith("\\ENDWHILE") || trimmed.StartsWith("\\REPEAT") ||
                trimmed.StartsWith("\\UNTIL") || trimmed.StartsWith("\\RETURN") ||
                trimmed.StartsWith("\\REQUIRE") || trimmed.StartsWith("\\ENSURE") ||
                trimmed.StartsWith("\\PRINT") || trimmed.StartsWith("\\COMMENT") ||
                trimmed.StartsWith("\\LOOP") || trimmed.StartsWith("\\ENDLOOP") ||
                trimmed.StartsWith("%"))
            {
                sb.AppendLine(line);
            }
            else
            {
                sb.AppendLine($@"\STATE {trimmed}");
            }
        }
        sb.AppendLine(@"\end{algorithmic}");
        sb.AppendLine(@"\end{algorithm}");
        return sb.ToString().TrimEnd();
    }

    private string RenderCalloutToLatex(JsonElement content)
    {
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var displayTitle = !string.IsNullOrEmpty(title) ? title : char.ToUpper(variant[0]) + variant[1..];
        return $@"\begin{{tcolorbox}}[title={{{EscapeLatex(displayTitle)}}}]
{ProcessLatexText(text)}
\end{{tcolorbox}}";
    }

    private string RenderFootnoteToLatex(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $@"\footnote{{{ProcessLatexText(text)}}}";
    }

    private static readonly string[] ParagraphMathEnvironments = [
        "align", "align*", "gather", "gather*", "multline", "multline*",
        "flalign", "flalign*", "alignat", "alignat*", "split",
    ];

    private string RenderEquationToLatex(JsonElement content)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";

        // Check both "equationMode" string and legacy "displayMode" boolean
        var equationMode = content.TryGetProperty("equationMode", out var em) ? em.GetString() ?? "display" : "display";
        var displayMode = equationMode != "inline"
            || (content.TryGetProperty("displayMode", out var d) && d.ValueKind == JsonValueKind.True);

        var numbered = !content.TryGetProperty("numbered", out var numProp) || numProp.ValueKind != JsonValueKind.False;

        // Strip MathLive placeholder artifacts
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");

        // Empty equation guard
        if (string.IsNullOrWhiteSpace(latex))
            return "% Empty equation";

        // Auto-detect paragraph-level math environments — these can't be wrapped in $...$
        var containsParagraphEnv = ParagraphMathEnvironments.Any(env =>
            latex.Contains($"\\begin{{{env}}}"));

        if (displayMode || containsParagraphEnv)
        {
            // If it already contains a math environment, handle numbering
            if (containsParagraphEnv)
            {
                if (!numbered)
                {
                    // Convert unstarred environments to starred variants
                    var result = latex;
                    foreach (var env in ParagraphMathEnvironments)
                    {
                        // Only convert non-starred environments to starred
                        if (env.EndsWith("*")) continue;
                        result = result.Replace($"\\begin{{{env}}}", $"\\begin{{{env}*}}");
                        result = result.Replace($"\\end{{{env}}}", $"\\end{{{env}*}}");
                    }
                    return result;
                }
                return latex;
            }

            var eqEnv = numbered ? "equation" : "equation*";
            return $@"\begin{{{eqEnv}}}
{latex}
\end{{{eqEnv}}}";
        }
        return $"${latex}$";
    }

    private string RenderFigureToLatex(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
        var width = content.TryGetProperty("width", out var w) ? w.GetDouble() : 0.8;
        var position = content.TryGetProperty("position", out var p) ? p.GetString() ?? "center" : "center";
        var placement = content.TryGetProperty("placement", out var pl) ? pl.GetString() ?? "auto" : "auto";

        // Map placement to LaTeX float specifier
        var floatSpec = placement switch
        {
            "here" => "[H]",
            "top" => "[t]",
            "bottom" => "[b]",
            "page" => "[p]",
            _ => "[htbp]" // "auto" or unrecognized
        };

        // Use a clean filename instead of the full URL for readability
        var displayPath = ExtractCleanImagePath(src);

        // Format width with invariant culture to avoid comma decimal separators
        var widthStr = width.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        var alignCommand = position switch
        {
            "left" => @"\raggedright",
            "right" => @"\raggedleft",
            _ => @"\centering"
        };

        var sb = new StringBuilder();

        // Check for subfigures
        if (content.TryGetProperty("subfigures", out var subfigures) && subfigures.ValueKind == JsonValueKind.Array && subfigures.GetArrayLength() > 0)
        {
            sb.AppendLine($@"\begin{{figure}}{floatSpec}");
            sb.AppendLine(alignCommand);

            var subfigCount = subfigures.GetArrayLength();
            var subfigWidth = subfigCount > 0 ? (1.0 / subfigCount) - 0.02 : 0.45;
            var subfigWidthStr = subfigWidth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            foreach (var subfig in subfigures.EnumerateArray())
            {
                var subSrc = subfig.TryGetProperty("src", out var ss) ? ss.GetString() ?? "" : "";
                var subCaption = subfig.TryGetProperty("caption", out var sc) ? sc.GetString() ?? "" : "";
                var subLabel = subfig.TryGetProperty("label", out var sl) ? sl.GetString() ?? "" : "";
                var subDisplayPath = ExtractCleanImagePath(subSrc);

                sb.AppendLine($@"\begin{{subfigure}}{{{subfigWidthStr}\textwidth}}");
                sb.AppendLine(@"\centering");
                if (!string.IsNullOrEmpty(subSrc))
                    sb.AppendLine($@"\includegraphics[width=\textwidth]{{{subDisplayPath}}}");
                else
                    sb.AppendLine(@"% [subfigure placeholder — no image uploaded]");
                if (!string.IsNullOrEmpty(subCaption))
                    sb.AppendLine($@"\caption{{{EscapeLatex(subCaption)}}}");
                if (!string.IsNullOrEmpty(subLabel))
                    sb.AppendLine($@"\label{{{subLabel}}}");
                sb.AppendLine(@"\end{subfigure}");
                sb.AppendLine(@"\hfill");
            }

            if (!string.IsNullOrEmpty(caption))
                sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}");
            if (!string.IsNullOrEmpty(label))
                sb.AppendLine($@"\label{{{label}}}");
            sb.AppendLine(@"\end{figure}");
        }
        else
        {
            sb.AppendLine($@"\begin{{figure}}{floatSpec}");
            sb.AppendLine(alignCommand);
            if (!string.IsNullOrEmpty(src))
                sb.AppendLine($@"\includegraphics[width={widthStr}\textwidth]{{{displayPath}}}");
            else
                sb.AppendLine(@"% [figure placeholder — no image uploaded]");
            if (!string.IsNullOrEmpty(caption))
            {
                sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}");
            }
            if (!string.IsNullOrEmpty(label))
            {
                sb.AppendLine($@"\label{{{label}}}");
            }
            sb.AppendLine(@"\end{figure}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a clean filename from an image URL or path for LaTeX preview.
    /// Converts long URLs like "/api/documents/.../assets/uuid" to "figures/uuid.png".
    /// </summary>
    private static string ExtractCleanImagePath(string src)
    {
        if (string.IsNullOrEmpty(src))
            return "(no image)";

        // Try to extract filename from URL
        try
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var last = segments.Length > 0 ? segments[^1] : "image";
                // If no extension, add .png
                if (!Path.HasExtension(last))
                    last = $"{last[..Math.Min(last.Length, 8)]}.png";
                return $"figures/{last}";
            }
        }
        catch { /* fall through */ }

        // For relative paths or API paths
        if (src.StartsWith("/api/") || src.StartsWith("http"))
        {
            var parts = src.Split('/');
            var last = parts.Length > 0 ? parts[^1] : "image";
            if (!Path.HasExtension(last))
                last = $"{last[..Math.Min(last.Length, 8)]}.png";
            return $"figures/{last}";
        }

        // Already a short path — keep as-is
        return src.Length > 60 ? $"figures/{Path.GetFileName(src)}" : src;
    }

    private string RenderTableToLatex(JsonElement content)
    {
        var sb = new StringBuilder();

        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var label = content.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
        var hasHeaders = content.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array && headers.GetArrayLength() > 0;

        if (content.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var rowList = rows.EnumerateArray().ToList();
            var colCount = hasHeaders
                ? headers.GetArrayLength()
                : rowList.Count > 0 && rowList[0].ValueKind == JsonValueKind.Array
                    ? rowList[0].GetArrayLength()
                    : 1;

            // 1.3 — Column alignment from content JSON, default to 'l'
            var colAlignments = Enumerable.Repeat("l", colCount).ToArray();
            if (content.TryGetProperty("columnAlign", out var alignProp) && alignProp.ValueKind == JsonValueKind.Array)
            {
                var alignList = alignProp.EnumerateArray().ToList();
                for (var i = 0; i < Math.Min(alignList.Count, colCount); i++)
                {
                    var val = alignList[i].GetString()?.ToLowerInvariant();
                    if (val is "l" or "c" or "r")
                        colAlignments[i] = val;
                }
            }
            var colSpec = string.Join("", colAlignments);

            // Build a rowspan tracker: coveredCells[row][col] = true if covered by a previous multirow
            var totalRows = (hasHeaders ? 1 : 0) + rowList.Count;
            var coveredCells = new bool[totalRows, colCount];

            sb.AppendLine(@"\begin{table}[htbp]");
            sb.AppendLine(@"\centering");
            sb.AppendLine(@"\renewcommand{\arraystretch}{1.3}");
            if (!string.IsNullOrEmpty(caption))
            {
                sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}");
            }
            if (!string.IsNullOrEmpty(label))
            {
                sb.AppendLine($@"\label{{{label}}}");
            }
            sb.AppendLine($@"\begin{{tabular}}{{{colSpec}}}");
            sb.AppendLine(@"\toprule");

            var currentRowIndex = 0;

            // Header row (bold)
            if (hasHeaders)
            {
                var headerCells = new List<string>();
                var colIdx = 0;
                foreach (var h in headers.EnumerateArray())
                {
                    if (colIdx >= colCount) break;
                    if (coveredCells[currentRowIndex, colIdx]) { colIdx++; headerCells.Add(""); continue; }

                    var cellText = GetCellText(h);
                    var colspan = GetCellIntProp(h, "colspan", 1);
                    var rowspan = GetCellIntProp(h, "rowspan", 1);
                    var rendered = $@"\textbf{{{EscapeLatex(cellText)}}}";

                    rendered = WrapLatexSpans(rendered, colspan, rowspan, colAlignments[colIdx], currentRowIndex, colIdx, colCount, coveredCells);
                    headerCells.Add(rendered);
                    colIdx += Math.Max(colspan, 1);
                }
                sb.AppendLine(string.Join(" & ", headerCells) + @" \\");
                sb.AppendLine(@"\midrule");
                currentRowIndex++;
            }

            // Data rows
            var isFirst = true;
            foreach (var row in rowList)
            {
                if (row.ValueKind == JsonValueKind.Array)
                {
                    var cells = new List<string>();
                    var colIdx = 0;
                    foreach (var cell in row.EnumerateArray())
                    {
                        if (colIdx >= colCount) break;
                        // Skip cells covered by a previous multirow
                        while (colIdx < colCount && coveredCells[currentRowIndex, colIdx])
                        {
                            cells.Add("");
                            colIdx++;
                        }
                        if (colIdx >= colCount) break;

                        var cellText = GetCellText(cell);
                        var colspan = GetCellIntProp(cell, "colspan", 1);
                        var rowspan = GetCellIntProp(cell, "rowspan", 1);
                        var rendered = EscapeLatex(cellText);

                        rendered = WrapLatexSpans(rendered, colspan, rowspan, colAlignments[colIdx], currentRowIndex, colIdx, colCount, coveredCells);
                        cells.Add(rendered);
                        colIdx += Math.Max(colspan, 1);
                    }
                    sb.AppendLine(string.Join(" & ", cells) + @" \\");

                    // If no explicit headers, treat first row as header
                    if (isFirst && !hasHeaders)
                    {
                        sb.AppendLine(@"\midrule");
                        isFirst = false;
                    }
                }
                currentRowIndex++;
            }

            sb.AppendLine(@"\bottomrule");
            sb.AppendLine(@"\end{tabular}");
            sb.AppendLine(@"\end{table}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract text from a cell that may be a plain string or an object with a "text" property.
    /// </summary>
    private static string GetCellText(JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.String)
            return cell.GetString() ?? "";
        if (cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty("text", out var t))
            return t.GetString() ?? "";
        return "";
    }

    /// <summary>
    /// Extract an integer property from a cell object, defaulting if the cell is a plain string.
    /// </summary>
    private static int GetCellIntProp(JsonElement cell, string propName, int defaultValue)
    {
        if (cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty(propName, out var prop) && prop.TryGetInt32(out var val))
            return val;
        return defaultValue;
    }

    /// <summary>
    /// Wrap rendered text in \multicolumn / \multirow as needed, and mark covered cells.
    /// </summary>
    private static string WrapLatexSpans(string rendered, int colspan, int rowspan, string alignment, int currentRow, int colIdx, int colCount, bool[,] coveredCells)
    {
        // Mark cells covered by rowspan
        if (rowspan > 1)
        {
            for (var r = 1; r < rowspan; r++)
            {
                for (var c = 0; c < Math.Max(colspan, 1); c++)
                {
                    var targetRow = currentRow + r;
                    var targetCol = colIdx + c;
                    if (targetRow < coveredCells.GetLength(0) && targetCol < coveredCells.GetLength(1))
                        coveredCells[targetRow, targetCol] = true;
                }
            }
            rendered = $@"\multirow{{{rowspan}}}{{*}}{{{rendered}}}";
        }

        if (colspan > 1)
        {
            rendered = $@"\multicolumn{{{colspan}}}{{{alignment}}}{{{rendered}}}";
        }

        return rendered;
    }

    private string RenderCodeToLatex(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var language = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var lineNumbers = content.TryGetProperty("lineNumbers", out var ln) && ln.ValueKind == JsonValueKind.True;
        var highlightLines = new List<int>();
        if (content.TryGetProperty("highlightLines", out var hl) && hl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in hl.EnumerateArray())
            {
                if (line.TryGetInt32(out var lineNum))
                    highlightLines.Add(lineNum);
            }
        }

        var options = new List<string>();
        if (!string.IsNullOrEmpty(language))
            options.Add($"language={language}");
        if (!string.IsNullOrEmpty(caption))
            options.Add($"caption={{{EscapeLatex(caption)}}}");
        if (lineNumbers)
            options.Add("numbers=left");
        if (highlightLines.Count > 0)
            options.Add($"emphstyle=\\color{{yellow}},emph={{{string.Join(",", highlightLines)}}}");

        var optionStr = options.Count > 0 ? $"[{string.Join(", ", options)}]" : "";
        return $@"\begin{{lstlisting}}{optionStr}
{code}
\end{{lstlisting}}";
    }

    private string RenderListToLatex(JsonElement content)
    {
        // Support both "listType" (string: "ordered"/"unordered") and "ordered" (boolean) formats
        var isOrdered = false;
        if (content.TryGetProperty("listType", out var lt))
        {
            isOrdered = lt.GetString() == "ordered";
        }
        else if (content.TryGetProperty("ordered", out var ord))
        {
            isOrdered = ord.ValueKind == JsonValueKind.True;
        }

        var env = isOrdered ? "enumerate" : "itemize";

        var sb = new StringBuilder();

        // Build enumitem options for ordered lists
        var enumOptions = new List<string>();
        if (isOrdered)
        {
            // Label format support
            if (content.TryGetProperty("labelFormat", out var lfProp))
            {
                var labelFormat = lfProp.GetString() ?? "number";
                var labelOption = labelFormat switch
                {
                    "alpha" => @"label=(\alph*)",
                    "Alpha" => @"label=(\Alph*)",
                    "roman" => @"label=(\roman*)",
                    "Roman" => @"label=(\Roman*)",
                    _ => (string?)null // "number" is default, no option needed
                };
                if (labelOption != null)
                    enumOptions.Add(labelOption);
            }

            // Start number support
            if (content.TryGetProperty("start", out var startProp) && startProp.TryGetInt32(out var startNum) && startNum != 1)
            {
                enumOptions.Add($"start={startNum}");
            }
        }

        if (enumOptions.Count > 0)
        {
            sb.AppendLine($@"\begin{{{env}}}[{string.Join(", ", enumOptions)}]");
        }
        else
        {
            sb.AppendLine($@"\begin{{{env}}}");
        }

        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                RenderListItemToLatex(item, isOrdered, sb);
            }
        }

        sb.AppendLine($@"\end{{{env}}}");
        return sb.ToString();
    }

    private void RenderListItemToLatex(JsonElement item, bool parentIsOrdered, StringBuilder sb)
    {
        // Support both string items and object items with text/richText/children properties
        string itemText;
        JsonElement? children = null;

        if (item.ValueKind == JsonValueKind.String)
        {
            itemText = item.GetString() ?? "";
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                itemText = textProp.GetString() ?? "";
            }
            else if (item.TryGetProperty("richText", out var richTextProp) && richTextProp.ValueKind == JsonValueKind.Array)
            {
                var textSb = new StringBuilder();
                foreach (var span in richTextProp.EnumerateArray())
                {
                    if (span.TryGetProperty("text", out var spanText) && spanText.ValueKind == JsonValueKind.String)
                    {
                        textSb.Append(spanText.GetString() ?? "");
                    }
                }
                itemText = textSb.ToString();
            }
            else
            {
                itemText = "";
            }

            // Check for nested children
            if (item.TryGetProperty("children", out var childrenProp) && childrenProp.ValueKind == JsonValueKind.Array && childrenProp.GetArrayLength() > 0)
            {
                children = childrenProp;
            }
        }
        else
        {
            itemText = "";
        }

        sb.AppendLine($@"\item {ProcessLatexText(itemText)}");

        // Render nested list if children present
        if (children.HasValue)
        {
            var nestedEnv = parentIsOrdered ? "enumerate" : "itemize";
            sb.AppendLine($@"\begin{{{nestedEnv}}}");
            foreach (var child in children.Value.EnumerateArray())
            {
                RenderListItemToLatex(child, parentIsOrdered, sb);
            }
            sb.AppendLine($@"\end{{{nestedEnv}}}");
        }
    }

    private string RenderBlockquoteToLatex(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $@"\begin{{quote}}
{ProcessLatexText(text)}
\end{{quote}}";
    }

    private string RenderTheoremToLatex(JsonElement content)
    {
        var theoremType = content.TryGetProperty("theoremType", out var tt) ? tt.GetString() ?? "theorem" : "theorem";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var numbered = !content.TryGetProperty("numbered", out var numProp) || numProp.ValueKind != JsonValueKind.False;
        var label = content.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "" : "";

        // Use starred variant for unnumbered theorems
        var envName = numbered ? theoremType : $"{theoremType}*";
        var titleOption = !string.IsNullOrEmpty(title) ? $"[{EscapeLatex(title)}]" : "";

        var sb = new StringBuilder();
        sb.AppendLine($@"\begin{{{envName}}}{titleOption}");
        if (!string.IsNullOrEmpty(label))
        {
            sb.AppendLine($@"\label{{{label}}}");
        }
        sb.AppendLine(ProcessLatexText(text));
        sb.Append($@"\end{{{envName}}}");
        return sb.ToString();
    }

    private string RenderBibliographyToLatex(IEnumerable<BibliographyEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{thebibliography}{99}");

        foreach (var entry in entries)
        {
            sb.AppendLine($@"\bibitem{{{entry.CiteKey}}}");

            // Try to build a citation string from the data
            try
            {
                var data = entry.Data?.RootElement;
                if (data.HasValue)
                {
                    var author = data.Value.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
                    var title = data.Value.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var year = data.Value.TryGetProperty("year", out var y) ? y.GetString() ?? "" : "";
                    var journal = data.Value.TryGetProperty("journal", out var j) ? j.GetString() ?? "" : "";

                    var citation = new List<string>();
                    if (!string.IsNullOrEmpty(author)) citation.Add(EscapeLatex(author));
                    if (!string.IsNullOrEmpty(title)) citation.Add($"\\textit{{{EscapeLatex(title)}}}");
                    if (!string.IsNullOrEmpty(journal)) citation.Add(EscapeLatex(journal));
                    if (!string.IsNullOrEmpty(year)) citation.Add($"({year})");

                    sb.AppendLine(string.Join(", ", citation) + ".");
                }
            }
            catch
            {
                sb.AppendLine($"{entry.CiteKey}.");
            }
        }

        sb.AppendLine(@"\end{thebibliography}");
        return sb.ToString();
    }

    private string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Escape special LaTeX characters
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

    private string ProcessLatexText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var result = text;

        // Step 1: Extract and protect math regions and LaTeX commands from escaping.
        // Replace $...$ and $$...$$ with placeholders to avoid mangling them.
        var mathRegions = new List<string>();
        result = Regex.Replace(result, @"\$\$(.+?)\$\$", m => {
            mathRegions.Add(m.Value);
            return $"\x00MATH{mathRegions.Count - 1}\x00";
        });
        result = Regex.Replace(result, @"(?<!\$)\$(?!\$)(.+?)(?<!\$)\$(?!\$)", m => {
            mathRegions.Add(m.Value);
            return $"\x00MATH{mathRegions.Count - 1}\x00";
        });

        // Protect inline LaTeX commands from the escape pass below. Without this, an
        // underscore inside e.g. \ce{H_2O} or \cite{smith_2020} would get escaped to \_
        // and break the chemistry / citation rendering on the LaTeX side.
        //
        // The regex matches \name[opts]?{arg1}{arg2}? for the command names we care
        // about. Listed explicitly (rather than catch-all) so we don't accidentally
        // protect markdown-converted commands the next step is about to produce.
        // Order in the alternation list — citations, refs, hyperref, chem, siunitx, misc.
        var commandRegions = new List<string>();
        const string protectedCommands =
            // Citations (natbib + biblatex flavors)
            "cite|citep|citet|citealp|citealt|parencite|textcite|footcite|autocite|nocite|" +
            // Cross-references
            "ref|eqref|cref|Cref|autoref|pageref|nameref|" +
            // Hyperref / links
            "url|href|hyperref|hypertarget|" +
            // Labels (must survive intact so cleveref can find them)
            "label|" +
            // mhchem (chemistry inline formulas)
            "ce|cee|cf|ch|" +
            // chemfig (chemistry structures)
            "chemfig|" +
            // siunitx (scientific units)
            "SI|si|num|unit|qty|" +
            // Footnotes (rare in paragraph text but can appear)
            "footnote";
        result = Regex.Replace(
            result,
            @"\\(?:" + protectedCommands + @")\*?(?:\[[^\]]*\])?\{[^{}]*\}(?:\{[^{}]*\})?",
            m =>
            {
                commandRegions.Add(m.Value);
                return $"\x00CMD{commandRegions.Count - 1}\x00";
            });

        // Step 2: Escape special LaTeX chars in the remaining text.
        // After Step 1 all math regions are replaced with placeholders so
        // the replacements below cannot accidentally touch math content.
        result = Regex.Replace(result, @"(?<!\\)([&%#])", @"\$1");
        // Escape underscores NOT part of __underline__ markers
        result = Regex.Replace(result, @"(?<!_)_(?!_)", @"\_");
        // ^ is only valid in math mode — escape to \textasciicircum{} in text
        result = result.Replace("^", @"\textasciicircum{}");
        // ~ produces a non-breaking space in LaTeX — escape to \textasciitilde{}
        result = result.Replace("~", @"\textasciitilde{}");
        // Escape any remaining literal $ that were not part of a $...$ math pair
        // (e.g. currency "$100"). These arrive here as plain $ because the math
        // protection in Step 1 only replaced matched pairs.
        result = result.Replace("$", @"\$");

        // Step 3: Convert inline formatting markers to LaTeX commands
        // Bold: **text** → \textbf{text}
        result = Regex.Replace(result, @"\*\*(.+?)\*\*", @"\textbf{$1}");
        // Italic: *text* → \emph{text}
        result = Regex.Replace(result, @"\*(.+?)\*", @"\emph{$1}");
        // Underline: __text__ → \underline{text}
        result = Regex.Replace(result, @"__(.+?)__", @"\underline{$1}");
        // Strikethrough: ~~text~~ → \st{text} (soul package)
        result = Regex.Replace(result, @"~~(.+?)~~", @"\st{$1}");
        // Inline code: `text` → \texttt{text}
        result = Regex.Replace(result, @"`(.+?)`", @"\texttt{$1}");

        // Step 4: Convert newlines to paragraph breaks
        result = Regex.Replace(result, @"\r?\n", "\n\n");

        // Step 5: Restore protected regions
        for (int i = 0; i < commandRegions.Count; i++)
            result = result.Replace($"\x00CMD{i}\x00", commandRegions[i]);
        for (int i = 0; i < mathRegions.Count; i++)
            result = result.Replace($"\x00MATH{i}\x00", mathRegions[i]);

        return result;
    }
}
