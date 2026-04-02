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

public class RenderService : IRenderService
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
        var displayMode = content.TryGetProperty("displayMode", out var d) && d.GetBoolean();

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
        html.Append($"<figure class=\"figure\" style=\"width: {widthPercent}%; text-align: {textAlign}\">");
        html.Append($"<img src=\"{srcEscaped}\" alt=\"{altEscaped}\" />");
        if (!string.IsNullOrEmpty(caption))
        {
            html.Append($"<figcaption>{captionEscaped}</figcaption>");
        }
        html.Append("</figure>");
        return html.ToString();
    }

    private string RenderTableToHtml(JsonElement content)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"table-container\"><table class=\"table\">");

        if (content.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var isFirst = true;
            foreach (var row in rows.EnumerateArray())
            {
                html.Append("<tr>");
                if (row.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cell in row.EnumerateArray())
                    {
                        var cellContent = cell.GetString() ?? "";
                        var escaped = WebUtility.HtmlEncode(cellContent);
                        var tag = isFirst ? "th" : "td";
                        html.Append($"<{tag}>{escaped}</{tag}>");
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

        // Support start number for ordered lists
        if (isOrdered && content.TryGetProperty("start", out var startProp) && startProp.TryGetInt32(out var startNum) && startNum != 1)
        {
            html.Append($"<{tag} class=\"list\" start=\"{startNum}\">");
        }
        else
        {
            html.Append($"<{tag} class=\"list\">");
        }

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

        // Preamble
        latex.AppendLine(@"\documentclass[11pt,a4paper]{article}");
        latex.AppendLine(LaTeXPreamble.Packages);

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
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{algorithm}");
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}");
        if (!string.IsNullOrEmpty(title))
            sb.AppendLine($@"\label{{{EscapeLatex(title)}}}");
        sb.AppendLine(@"\begin{algorithmic}");
        sb.AppendLine(code);
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
        var displayMode = content.TryGetProperty("displayMode", out var d) && d.GetBoolean();

        // Strip MathLive placeholder artifacts
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");

        // Auto-detect paragraph-level math environments — these can't be wrapped in $...$
        var containsParagraphEnv = ParagraphMathEnvironments.Any(env =>
            latex.Contains($"\\begin{{{env}}}"));

        if (displayMode || containsParagraphEnv)
        {
            // If it already contains a math environment, don't wrap in equation
            if (containsParagraphEnv)
                return latex;

            return $@"\begin{{equation}}
{latex}
\end{{equation}}";
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
        sb.AppendLine(@"\begin{figure}[htbp]");
        sb.AppendLine(alignCommand);
        sb.AppendLine($@"\includegraphics[width={widthStr}\textwidth]{{{displayPath}}}");
        if (!string.IsNullOrEmpty(caption))
        {
            sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}");
        }
        if (!string.IsNullOrEmpty(label))
        {
            sb.AppendLine($@"\label{{{label}}}");
        }
        sb.AppendLine(@"\end{figure}");
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
        var hasHeaders = content.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array && headers.GetArrayLength() > 0;

        if (content.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var rowList = rows.EnumerateArray().ToList();
            var colCount = hasHeaders
                ? headers.GetArrayLength()
                : rowList.Count > 0 && rowList[0].ValueKind == JsonValueKind.Array
                    ? rowList[0].GetArrayLength()
                    : 1;
            var colSpec = string.Join("", Enumerable.Repeat("l", colCount));

            sb.AppendLine(@"\begin{table}[htbp]");
            sb.AppendLine(@"\centering");
            sb.AppendLine(@"\renewcommand{\arraystretch}{1.3}");
            if (!string.IsNullOrEmpty(caption))
            {
                sb.AppendLine($@"\caption{{{EscapeLatex(caption)}}}");
            }
            sb.AppendLine($@"\begin{{tabular}}{{{colSpec}}}");
            sb.AppendLine(@"\toprule");

            // Header row (bold)
            if (hasHeaders)
            {
                var headerCells = headers.EnumerateArray()
                    .Select(h => $@"\textbf{{{EscapeLatex(h.GetString() ?? "")}}}")
                    .ToList();
                sb.AppendLine(string.Join(" & ", headerCells) + @" \\");
                sb.AppendLine(@"\midrule");
            }

            // Data rows
            var isFirst = true;
            foreach (var row in rowList)
            {
                if (row.ValueKind == JsonValueKind.Array)
                {
                    var cells = row.EnumerateArray()
                        .Select(c => EscapeLatex(c.GetString() ?? ""))
                        .ToList();
                    sb.AppendLine(string.Join(" & ", cells) + @" \\");

                    // If no explicit headers, treat first row as header
                    if (isFirst && !hasHeaders)
                    {
                        sb.AppendLine(@"\midrule");
                        isFirst = false;
                    }
                }
            }

            sb.AppendLine(@"\bottomrule");
            sb.AppendLine(@"\end{tabular}");
            sb.AppendLine(@"\end{table}");
        }

        return sb.ToString();
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

        // Support start number for ordered lists (requires enumitem package)
        if (isOrdered && content.TryGetProperty("start", out var startProp) && startProp.TryGetInt32(out var startNum) && startNum != 1)
        {
            sb.AppendLine($@"\begin{{{env}}}[start={startNum}]");
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

        // Protect LaTeX commands: \cite{...}, \ref{...}, \cref{...}, \url{...}, \href{...}{...}, \label{...}
        var commandRegions = new List<string>();
        result = Regex.Replace(result, @"\\(?:cite|ref|cref|Cref|url|label|eqref)\{[^}]+\}", m => {
            commandRegions.Add(m.Value);
            return $"\x00CMD{commandRegions.Count - 1}\x00";
        });
        result = Regex.Replace(result, @"\\href\{[^}]+\}\{[^}]+\}", m => {
            commandRegions.Add(m.Value);
            return $"\x00CMD{commandRegions.Count - 1}\x00";
        });

        // Step 2: Escape special LaTeX chars in the remaining text
        result = Regex.Replace(result, @"(?<!\\)([&%#])", @"\$1");
        // Escape underscores NOT inside formatting markers
        result = Regex.Replace(result, @"(?<!_)_(?!_)", @"\_");

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
