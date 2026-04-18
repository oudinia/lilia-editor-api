using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public partial class RenderService
{
    public async Task<string> RenderToMarkdownAsync(Guid documentId)
    {
        var doc = await _context.Documents.FindAsync(documentId);
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var hasBibliography = blocks.Any(b => string.Equals(b.Type, "bibliography", StringComparison.OrdinalIgnoreCase));
        List<BibliographyEntry> bibEntries = new();
        if (hasBibliography)
        {
            bibEntries = await _context.BibliographyEntries
                .Where(e => e.DocumentId == documentId)
                .OrderBy(e => e.CiteKey)
                .ToListAsync();
        }

        var sb = new StringBuilder();

        // YAML frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"title: {MdYamlEscape(doc?.Title ?? "Untitled")}");
        if (!string.IsNullOrEmpty(doc?.Language))
            sb.AppendLine($"lang: {doc.Language}");
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var block in blocks)
        {
            string rendered;
            if (string.Equals(block.Type, "bibliography", StringComparison.OrdinalIgnoreCase))
            {
                rendered = RenderBibliographyEntriesToMarkdown(bibEntries);
            }
            else
            {
                rendered = RenderBlockToMarkdown(block);
            }
            if (!string.IsNullOrEmpty(rendered))
            {
                sb.AppendLine(rendered);
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    public string RenderBlockToMarkdown(Block block)
    {
        try
        {
            var content = block.Content.RootElement;
            return block.Type.ToLowerInvariant() switch
            {
                "heading" or "header" => RenderHeadingToMarkdown(content, ReadLabel(content)),
                "paragraph" => RenderParagraphToMarkdown(content),
                "equation" => RenderEquationToMarkdown(content, ReadLabel(content)),
                "figure" or "image" => RenderFigureToMarkdown(content),
                "table" => RenderTableToMarkdown(content),
                "code" => RenderCodeToMarkdown(content),
                "list" => RenderListToMarkdown(content),
                "blockquote" or "quote" => RenderBlockquoteToMarkdown(content),
                "theorem" => RenderTheoremToMarkdown(content, ReadLabel(content)),
                "abstract" => RenderAbstractToMarkdown(content),
                "tableofcontents" => "## Table of Contents",
                "bibliography" => "",
                "pagebreak" or "divider" or "columnbreak" => "---",
                "columnlayout" => RenderColumnLayoutToMarkdown(content),
                "embed" => RenderEmbedToMarkdown(content),
                "algorithm" => RenderAlgorithmToMarkdown(content),
                "callout" => RenderCalloutToMarkdown(content),
                "footnote" => RenderFootnoteToMarkdown(content, ReadLabel(content)),
                _ => $"<!-- Unsupported block type: {block.Type} -->"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to Markdown", block.Id);
            return $"<!-- Error rendering block {block.Id} -->";
        }
    }

    private static string RenderHeadingToMarkdown(JsonElement content, string? label)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var level = content.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 1;
        level = Math.Clamp(level, 1, 6);
        var prefix = new string('#', level);
        var anchor = string.IsNullOrEmpty(label) ? "" : $" {{#{label}}}";
        return $"{prefix} {ProcessMarkdownInline(text)}{anchor}";
    }

    private static string RenderParagraphToMarkdown(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return ProcessMarkdownInline(text);
    }

    private static string RenderEquationToMarkdown(JsonElement content, string? label)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");

        var equationMode = content.TryGetProperty("equationMode", out var em) ? em.GetString() ?? "display" : "display";
        if (string.Equals(equationMode, "inline", StringComparison.OrdinalIgnoreCase))
        {
            return $"${latex}$";
        }

        // Display math — preserve LaTeX verbatim inside $$...$$
        var body = latex;
        if (!string.IsNullOrEmpty(label))
        {
            // Inject \label{} so MathJax/KaTeX renderers that support it can wire up refs
            body = $"\\label{{{label}}}\n{body}";
        }
        return $"$$\n{body}\n$$";
    }

    private static string RenderFigureToMarkdown(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var alt = content.TryGetProperty("alt", out var a) ? a.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(src)) return "";

        var altText = !string.IsNullOrEmpty(alt) ? alt : caption;
        var md = $"![{EscapeMarkdownBrackets(altText)}]({src})";
        if (!string.IsNullOrEmpty(caption))
        {
            md += $"\n\n*{ProcessMarkdownInline(caption)}*";
        }
        return md;
    }

    private static string RenderTableToMarkdown(JsonElement content)
    {
        if (!content.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return "";

        var headers = new List<string>();
        if (content.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in hdrs.EnumerateArray())
            {
                headers.Add(h.GetString() ?? "");
            }
        }

        var rows = new List<List<string>>();
        foreach (var row in rowsEl.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) continue;
            var cells = new List<string>();
            foreach (var cell in row.EnumerateArray())
            {
                string text;
                if (cell.ValueKind == JsonValueKind.String)
                {
                    text = cell.GetString() ?? "";
                }
                else if (cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty("content", out var cc))
                {
                    text = cc.GetString() ?? "";
                }
                else
                {
                    text = "";
                }
                cells.Add(text.Replace("|", "\\|").Replace("\n", " "));
            }
            rows.Add(cells);
        }

        if (headers.Count == 0 && rows.Count > 0)
        {
            // Fall back to first row as header
            headers = rows[0];
            rows = rows.Skip(1).ToList();
        }
        if (headers.Count == 0) return "";

        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        sb.Append("|").Append(string.Concat(Enumerable.Repeat("---|", headers.Count))).AppendLine();
        foreach (var row in rows)
        {
            // Pad or trim to header length
            while (row.Count < headers.Count) row.Add("");
            sb.Append("| ").Append(string.Join(" | ", row.Take(headers.Count))).AppendLine(" |");
        }

        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        if (!string.IsNullOrEmpty(caption))
        {
            sb.AppendLine();
            sb.Append("*Table: ").Append(ProcessMarkdownInline(caption)).Append('*');
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderCodeToMarkdown(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var lang = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var md = $"```{lang}\n{code}\n```";
        if (!string.IsNullOrEmpty(caption)) md += $"\n\n*{ProcessMarkdownInline(caption)}*";
        return md;
    }

    private static string RenderListToMarkdown(JsonElement content)
    {
        var ordered = content.TryGetProperty("ordered", out var o) && o.ValueKind == JsonValueKind.True;
        var start = content.TryGetProperty("start", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 1;

        // Prefer richItems (nested) if present
        if (content.TryGetProperty("richItems", out var rich) && rich.ValueKind == JsonValueKind.Array && rich.GetArrayLength() > 0)
        {
            return RenderRichListItems(rich, ordered, 0, start);
        }

        if (!content.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();
        var idx = 0;
        foreach (var item in items.EnumerateArray())
        {
            var text = item.GetString() ?? "";
            var marker = ordered ? $"{start + idx}." : "-";
            sb.AppendLine($"{marker} {ProcessMarkdownInline(text)}");
            idx++;
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderRichListItems(JsonElement items, bool ordered, int depth, int start)
    {
        var sb = new StringBuilder();
        var indent = new string(' ', depth * 2);
        var idx = 0;
        foreach (var item in items.EnumerateArray())
        {
            var text = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var marker = ordered ? $"{start + idx}." : "-";
            sb.AppendLine($"{indent}{marker} {ProcessMarkdownInline(text)}");
            if (item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
            {
                sb.Append(RenderRichListItems(children, ordered, depth + 1, 1));
            }
            idx++;
        }
        return sb.ToString();
    }

    private static string RenderBlockquoteToMarkdown(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var attribution = content.TryGetProperty("attribution", out var a) ? a.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            sb.AppendLine($"> {ProcessMarkdownInline(line)}");
        }
        if (!string.IsNullOrEmpty(attribution))
        {
            sb.AppendLine(">");
            sb.AppendLine($"> — {ProcessMarkdownInline(attribution)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderTheoremToMarkdown(JsonElement content, string? label)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var theoremType = content.TryGetProperty("theoremType", out var tt) ? tt.GetString() ?? "Theorem" : "Theorem";
        var title = content.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var header = string.IsNullOrEmpty(title) ? $"**{theoremType}**" : $"**{theoremType}** ({title})";
        var anchor = string.IsNullOrEmpty(label) ? "" : $" {{#{label}}}";
        return $"> {header}{anchor}\n>\n> {ProcessMarkdownInline(text).Replace("\n", "\n> ")}";
    }

    private static string RenderAbstractToMarkdown(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"**Abstract.** {ProcessMarkdownInline(text)}";
    }

    private static string RenderEmbedToMarkdown(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var engine = content.TryGetProperty("engine", out var e) ? e.GetString() ?? "latex" : "latex";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var md = $"```{engine}\n{code}\n```";
        if (!string.IsNullOrEmpty(caption)) md += $"\n\n*{ProcessMarkdownInline(caption)}*";
        return md;
    }

    private static string RenderAlgorithmToMarkdown(JsonElement content)
    {
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var code = content.TryGetProperty("code", out var cd) ? cd.GetString() ?? "" : "";
        var header = !string.IsNullOrEmpty(caption) ? caption : (string.IsNullOrEmpty(title) ? "Algorithm" : title);
        return $"**{header}**\n\n```\n{code}\n```";
    }

    private static string RenderCalloutToMarkdown(JsonElement content)
    {
        var type = content.TryGetProperty("calloutType", out var ct) ? ct.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var header = string.IsNullOrEmpty(title) ? char.ToUpper(type[0]) + type[1..] : title;
        var sb = new StringBuilder();
        // GitHub alert syntax — widely supported
        sb.AppendLine($"> [!{type.ToUpperInvariant()}] **{header}**");
        foreach (var line in text.Split('\n'))
        {
            sb.AppendLine($"> {ProcessMarkdownInline(line)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderFootnoteToMarkdown(JsonElement content, string? label)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var key = !string.IsNullOrEmpty(label) ? label : "fn";
        return $"[^{key}]: {ProcessMarkdownInline(text)}";
    }

    private static string RenderBibliographyEntriesToMarkdown(List<BibliographyEntry> entries)
    {
        if (entries.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("## References");
        sb.AppendLine();
        foreach (var entry in entries)
        {
            var author = ReadBibField(entry, "author");
            var title = ReadBibField(entry, "title");
            var year = ReadBibField(entry, "year");
            var journal = ReadBibField(entry, "journal");
            var booktitle = ReadBibField(entry, "booktitle");
            var volume = ReadBibField(entry, "volume");
            var pages = ReadBibField(entry, "pages");
            var publisher = ReadBibField(entry, "publisher");
            var doi = ReadBibField(entry, "doi");
            var url = ReadBibField(entry, "url");

            var line = new StringBuilder();
            line.Append($"[{entry.CiteKey}]: ");
            if (!string.IsNullOrEmpty(author)) line.Append(author).Append(' ');
            if (!string.IsNullOrEmpty(year)) line.Append('(').Append(year).Append("). ");
            if (!string.IsNullOrEmpty(title)) line.Append('*').Append(title).Append("*. ");
            if (!string.IsNullOrEmpty(journal))
            {
                line.Append(journal);
                if (!string.IsNullOrEmpty(volume)) line.Append(", ").Append(volume);
                if (!string.IsNullOrEmpty(pages)) line.Append(", ").Append(pages);
                line.Append('.');
            }
            else if (!string.IsNullOrEmpty(booktitle))
            {
                line.Append("In *").Append(booktitle).Append('*');
                if (!string.IsNullOrEmpty(pages)) line.Append(", pp. ").Append(pages);
                line.Append('.');
            }
            if (!string.IsNullOrEmpty(publisher)) line.Append(' ').Append(publisher).Append('.');
            if (!string.IsNullOrEmpty(doi)) line.Append(" https://doi.org/").Append(doi);
            else if (!string.IsNullOrEmpty(url)) line.Append(' ').Append(url);

            sb.AppendLine(line.ToString());
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string ReadBibField(BibliographyEntry entry, string fieldName)
    {
        var data = entry.Data.RootElement;
        if (data.ValueKind != JsonValueKind.Object) return "";
        if (data.TryGetProperty(fieldName, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? "";
        return "";
    }

    // ------------------------------------------------------------------------
    // Inline transformations — LML inline syntax → Markdown
    // Preserves $math$ and `code` verbatim; converts *bold* → **bold**,
    // _italic_ → *italic*, citations/refs/urls to Markdown equivalents.
    // ------------------------------------------------------------------------
    private static string ProcessMarkdownInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = text;

        // Protect inline math first so no replacement touches LaTeX content
        var mathProtected = new List<string>();
        result = Regex.Replace(result, @"\$[^$\n]+\$", m =>
        {
            mathProtected.Add(m.Value);
            return $"\x00M{mathProtected.Count - 1}\x00";
        });

        // Protect inline code spans
        var codeProtected = new List<string>();
        result = Regex.Replace(result, @"`[^`\n]+`", m =>
        {
            codeProtected.Add(m.Value);
            return $"\x00C{codeProtected.Count - 1}\x00";
        });

        // LML bold *x* → Markdown **x**
        result = Regex.Replace(result, @"\*([^*\n]+)\*", "**$1**");
        // LML italic _x_ → Markdown *x*
        result = Regex.Replace(result, @"(?<![\w])_([^_\n]+)_(?![\w])", "*$1*");

        // Citations: @cite{k1,k2} or \cite{k1,k2} → [@k1; @k2]
        result = Regex.Replace(result, @"[@\\]cite\{([^}]+)\}", m =>
        {
            var keys = m.Groups[1].Value.Split(',').Select(k => "@" + k.Trim()).ToArray();
            return "[" + string.Join("; ", keys) + "]";
        });

        // References: @ref{label}, \ref{label}, \cref{label}, etc. → [label](#label)
        result = Regex.Replace(result, @"[@\\](?:c|C|eq|auto|page|name)?ref\{([^}]+)\}", m => $"[{m.Groups[1].Value}](#{m.Groups[1].Value})");

        // URL: \url{u} → <u>
        result = Regex.Replace(result, @"\\url\{([^}]+)\}", "<$1>");

        // Restore protected regions
        for (int i = codeProtected.Count - 1; i >= 0; i--)
            result = result.Replace($"\x00C{i}\x00", codeProtected[i]);
        for (int i = mathProtected.Count - 1; i >= 0; i--)
            result = result.Replace($"\x00M{i}\x00", mathProtected[i]);

        return result;
    }

    private static string MdYamlEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var needsQuoting = s.Contains(':') || s.Contains('#') || s.Contains('"') || s.StartsWith("- ") || s.StartsWith("? ");
        if (!needsQuoting) return s;
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string EscapeMarkdownBrackets(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("[", "\\[").Replace("]", "\\]");
    }

    private static string RenderColumnLayoutToMarkdown(JsonElement content)
    {
        // Markdown has no native multi-column — emit an HTML fragment that
        // survives GFM+HTML passthrough.
        var mode = content.TryGetProperty("mode", out var m) ? m.GetString() ?? "start" : "start";
        if (string.Equals(mode, "end", StringComparison.OrdinalIgnoreCase))
            return "</div>";
        var columns = content.TryGetProperty("columns", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 2;
        columns = Math.Clamp(columns, 1, 3);
        return $"<div style=\"column-count: {columns};\">";
    }

    private static string? ReadLabel(JsonElement content)
    {
        return content.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString()
            : null;
    }
}
