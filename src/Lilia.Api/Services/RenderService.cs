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

        var html = new StringBuilder();
        html.Append($"<div class=\"lilia-page\" data-page=\"{page}\">");

        foreach (var block in blocks)
        {
            html.Append(RenderBlockToHtml(block));
        }

        html.Append("</div>");
        return html.ToString();
    }

    public async Task<string> RenderToHtmlAsync(Guid documentId)
    {
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var html = new StringBuilder();
        html.Append("<div class=\"lilia-preview\">");

        foreach (var block in blocks)
        {
            html.Append(RenderBlockToHtml(block));
        }

        html.Append("</div>");
        return html.ToString();
    }

    public string RenderBlockToHtml(Block block)
    {
        try
        {
            var content = block.Content.RootElement;

            return block.Type switch
            {
                "heading" => RenderHeadingToHtml(content),
                "paragraph" => RenderParagraphToHtml(content),
                "equation" => RenderEquationToHtml(content),
                "figure" => RenderFigureToHtml(content),
                "table" => RenderTableToHtml(content),
                "code" => RenderCodeToHtml(content),
                "list" => RenderListToHtml(content),
                "blockquote" => RenderBlockquoteToHtml(content),
                "theorem" => RenderTheoremToHtml(content),
                "bibliography" => RenderBibliographyToHtml(content),
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
        var escaped = WebUtility.HtmlEncode(latex);

        if (displayMode)
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

        var srcEscaped = WebUtility.HtmlEncode(src);
        var altEscaped = WebUtility.HtmlEncode(alt);
        var captionEscaped = WebUtility.HtmlEncode(caption);

        var html = new StringBuilder();
        html.Append("<figure class=\"figure\">");
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
        var escaped = WebUtility.HtmlEncode(code);
        var langAttr = !string.IsNullOrEmpty(language) ? $" data-language=\"{WebUtility.HtmlEncode(language)}\"" : "";

        return $"<pre class=\"code-block\"{langAttr}><code>{escaped}</code></pre>";
    }

    private string RenderListToHtml(JsonElement content)
    {
        var listType = content.TryGetProperty("listType", out var lt) ? lt.GetString() ?? "unordered" : "unordered";
        var tag = listType == "ordered" ? "ol" : "ul";

        var html = new StringBuilder();
        html.Append($"<{tag} class=\"list\">");

        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var itemText = item.GetString() ?? "";
                var escaped = ProcessInlineContent(itemText);
                html.Append($"<li>{escaped}</li>");
            }
        }

        html.Append($"</{tag}>");
        return html.ToString();
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

        var typeEscaped = WebUtility.HtmlEncode(theoremType);
        var titleEscaped = WebUtility.HtmlEncode(title);
        var processed = ProcessInlineContent(text);

        var html = new StringBuilder();
        html.Append($"<div class=\"theorem theorem-{typeEscaped}\">");
        html.Append($"<div class=\"theorem-header\"><strong>{char.ToUpper(typeEscaped[0])}{typeEscaped[1..]}</strong>");
        if (!string.IsNullOrEmpty(title))
        {
            html.Append($" ({titleEscaped})");
        }
        html.Append(".</div>");
        html.Append($"<div class=\"theorem-body\">{processed}</div>");
        html.Append("</div>");
        return html.ToString();
    }

    private string RenderBibliographyToHtml(JsonElement content)
    {
        return "<div class=\"bibliography\"><h3>References</h3><div class=\"bibliography-entries\"></div></div>";
    }

    private string ProcessInlineContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // First escape HTML
        var result = WebUtility.HtmlEncode(text);

        // Process inline math: $...$ (but not $$...$$)
        result = Regex.Replace(result, @"(?<!\$)\$(?!\$)(.+?)(?<!\$)\$(?!\$)",
            m => $"<span class=\"inline-math\" data-latex=\"{m.Groups[1].Value}\">${m.Groups[1].Value}$</span>");

        // Process bold: **...**
        result = Regex.Replace(result, @"\*\*(.+?)\*\*",
            m => $"<strong>{m.Groups[1].Value}</strong>");

        // Process italic: *...*
        result = Regex.Replace(result, @"\*(.+?)\*",
            m => $"<em>{m.Groups[1].Value}</em>");

        // Process citations: \cite{...}
        result = Regex.Replace(result, @"\\cite\{(.+?)\}",
            m => $"<cite data-cite=\"{m.Groups[1].Value}\">[{m.Groups[1].Value}]</cite>");

        // Process references: \ref{...}
        result = Regex.Replace(result, @"\\ref\{(.+?)\}",
            m => $"<a class=\"ref\" data-ref=\"{m.Groups[1].Value}\">{m.Groups[1].Value}</a>");

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
        latex.AppendLine(@"\usepackage[utf8]{inputenc}");
        latex.AppendLine(@"\usepackage{amsmath,amssymb,amsthm}");
        latex.AppendLine(@"\usepackage{graphicx}");
        latex.AppendLine(@"\usepackage{hyperref}");
        latex.AppendLine(@"\usepackage{listings}");
        latex.AppendLine(@"\usepackage{booktabs}");
        latex.AppendLine();

        // Theorem environments
        latex.AppendLine(@"\newtheorem{theorem}{Theorem}");
        latex.AppendLine(@"\newtheorem{lemma}{Lemma}");
        latex.AppendLine(@"\newtheorem{proposition}{Proposition}");
        latex.AppendLine(@"\newtheorem{corollary}{Corollary}");
        latex.AppendLine(@"\newtheorem{definition}{Definition}");
        latex.AppendLine(@"\newtheorem{example}{Example}");
        latex.AppendLine(@"\newtheorem{remark}{Remark}");
        latex.AppendLine();

        latex.AppendLine($@"\title{{{EscapeLatex(doc.Title)}}}");
        latex.AppendLine(@"\begin{document}");
        latex.AppendLine(@"\maketitle");
        latex.AppendLine();

        // Blocks
        foreach (var block in doc.Blocks)
        {
            latex.AppendLine(RenderBlockToLatex(block));
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

            return block.Type switch
            {
                "heading" => RenderHeadingToLatex(content),
                "paragraph" => RenderParagraphToLatex(content),
                "equation" => RenderEquationToLatex(content),
                "figure" => RenderFigureToLatex(content),
                "table" => RenderTableToLatex(content),
                "code" => RenderCodeToLatex(content),
                "list" => RenderListToLatex(content),
                "blockquote" => RenderBlockquoteToLatex(content),
                "theorem" => RenderTheoremToLatex(content),
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

    private string RenderEquationToLatex(JsonElement content)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";
        var displayMode = content.TryGetProperty("displayMode", out var d) && d.GetBoolean();

        if (displayMode)
        {
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

        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{figure}[htbp]");
        sb.AppendLine(@"\centering");
        sb.AppendLine($@"\includegraphics[width=0.8\textwidth]{{{src}}}");
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

    private string RenderTableToLatex(JsonElement content)
    {
        var sb = new StringBuilder();

        if (content.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var rowList = rows.EnumerateArray().ToList();
            if (rowList.Count > 0)
            {
                var firstRow = rowList[0];
                var colCount = firstRow.ValueKind == JsonValueKind.Array ? firstRow.GetArrayLength() : 1;
                var colSpec = string.Join("", Enumerable.Repeat("c", colCount));

                sb.AppendLine(@"\begin{table}[htbp]");
                sb.AppendLine(@"\centering");
                sb.AppendLine($@"\begin{{tabular}}{{{colSpec}}}");
                sb.AppendLine(@"\toprule");

                var isFirst = true;
                foreach (var row in rowList)
                {
                    if (row.ValueKind == JsonValueKind.Array)
                    {
                        var cells = row.EnumerateArray()
                            .Select(c => EscapeLatex(c.GetString() ?? ""))
                            .ToList();
                        sb.AppendLine(string.Join(" & ", cells) + @" \\");

                        if (isFirst)
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
        }

        return sb.ToString();
    }

    private string RenderCodeToLatex(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var language = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";

        var langOption = !string.IsNullOrEmpty(language) ? $"[language={language}]" : "";
        return $@"\begin{{lstlisting}}{langOption}
{code}
\end{{lstlisting}}";
    }

    private string RenderListToLatex(JsonElement content)
    {
        var listType = content.TryGetProperty("listType", out var lt) ? lt.GetString() ?? "unordered" : "unordered";
        var env = listType == "ordered" ? "enumerate" : "itemize";

        var sb = new StringBuilder();
        sb.AppendLine($@"\begin{{{env}}}");

        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var itemText = item.GetString() ?? "";
                sb.AppendLine($@"\item {ProcessLatexText(itemText)}");
            }
        }

        sb.AppendLine($@"\end{{{env}}}");
        return sb.ToString();
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

        var titleOption = !string.IsNullOrEmpty(title) ? $"[{EscapeLatex(title)}]" : "";
        return $@"\begin{{{theoremType}}}{titleOption}
{ProcessLatexText(text)}
\end{{{theoremType}}}";
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

        // Don't escape math delimiters and common LaTeX commands
        // Just escape the text parts between math and commands

        // For simplicity, we'll do basic escaping but preserve $...$ and common commands
        var result = text;

        // Escape special chars except those used in LaTeX commands and math
        result = Regex.Replace(result, @"(?<!\\)([&%#])", @"\$1");
        result = result.Replace("_", @"\_");

        return result;
    }
}
