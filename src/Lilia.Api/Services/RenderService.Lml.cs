using System.Text;
using System.Text.Json;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public partial class RenderService
{
    public async Task<string> RenderToLmlAsync(Guid documentId)
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

        // Document header
        sb.AppendLine("@document");
        sb.AppendLine($"title: {doc?.Title ?? "Untitled"}");
        if (!string.IsNullOrEmpty(doc?.Language)) sb.AppendLine($"language: {doc.Language}");
        if (!string.IsNullOrEmpty(doc?.PaperSize)) sb.AppendLine($"paperSize: {doc.PaperSize}");
        if (!string.IsNullOrEmpty(doc?.FontFamily)) sb.AppendLine($"fontFamily: {doc.FontFamily}");
        if (doc != null) sb.AppendLine($"fontSize: {doc.FontSize}");
        sb.AppendLine();

        foreach (var block in blocks)
        {
            string rendered;
            if (string.Equals(block.Type, "bibliography", StringComparison.OrdinalIgnoreCase))
            {
                rendered = RenderBibliographyEntriesToLml(bibEntries);
            }
            else
            {
                rendered = RenderBlockToLml(block);
            }
            if (!string.IsNullOrEmpty(rendered))
            {
                sb.AppendLine(rendered);
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    public string RenderBlockToLml(Block block)
    {
        try
        {
            var content = block.Content.RootElement;
            var label = ReadLmlLabel(content);
            return block.Type.ToLowerInvariant() switch
            {
                "heading" or "header" => RenderHeadingToLml(content, label),
                "paragraph" => RenderParagraphToLml(content),
                "equation" => RenderEquationToLml(content, label),
                "figure" or "image" => RenderFigureToLml(content, label),
                "table" => RenderTableToLml(content, label),
                "code" => RenderCodeToLml(content, label),
                "list" => RenderListToLml(content),
                "blockquote" or "quote" => RenderBlockquoteToLml(content),
                "theorem" => RenderTheoremToLml(content, label),
                "abstract" => RenderAbstractToLml(content),
                "tableofcontents" => "@toc",
                "bibliography" => "",
                "pagebreak" or "divider" => "@pagebreak",
                "columnbreak" => "@columnbreak",
                "columnlayout" => RenderColumnLayoutToLml(content),
                "embed" => RenderEmbedToLml(content, label),
                "algorithm" => RenderAlgorithmToLml(content, label),
                "callout" => RenderCalloutToLml(content, label),
                "footnote" => RenderFootnoteToLml(content, label),
                _ => $"@{block.Type.ToLowerInvariant()}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to LML", block.Id);
            return "";
        }
    }

    private static string RenderHeadingToLml(JsonElement content, string? label)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var level = content.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 1;
        level = Math.Clamp(level, 1, 6);
        var prefix = new string('#', level);
        var anchor = string.IsNullOrEmpty(label) ? "" : $" {{#{label}}}";
        return $"{prefix} {text}{anchor}";
    }

    private static string RenderParagraphToLml(JsonElement content)
    {
        // Paragraph text is already in LML inline syntax — emit as-is
        return content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    }

    private static string RenderEquationToLml(JsonElement content, string? label)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";
        var equationMode = content.TryGetProperty("equationMode", out var em) ? em.GetString() ?? "display" : "display";
        var attrs = BuildAttrs(("label", label), ("mode", string.Equals(equationMode, "inline", StringComparison.OrdinalIgnoreCase) ? "inline" : null));
        return $"@equation{attrs}\n{latex}";
    }

    private static string RenderFigureToLml(JsonElement content, string? label)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var alt = content.TryGetProperty("alt", out var a) ? a.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var width = content.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble().ToString("0.##") : null;
        var position = content.TryGetProperty("position", out var p) ? p.GetString() : null;
        var attrs = BuildAttrs(
            ("src", src),
            ("alt", string.IsNullOrEmpty(alt) ? null : alt),
            ("caption", string.IsNullOrEmpty(caption) ? null : caption),
            ("width", width),
            ("position", position),
            ("label", label));
        return $"@figure{attrs}";
    }

    private static string RenderTableToLml(JsonElement content, string? label)
    {
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        var attrs = BuildAttrs(("caption", string.IsNullOrEmpty(caption) ? null : caption), ("label", label));
        var sb = new StringBuilder();
        sb.AppendLine($"@table{attrs}");

        var headers = new List<string>();
        if (content.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in hdrs.EnumerateArray())
                headers.Add(h.GetString() ?? "");
        }
        if (headers.Count > 0)
        {
            sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
            sb.Append("|").Append(string.Concat(Enumerable.Repeat("---|", headers.Count))).AppendLine();
        }

        if (content.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var cells = new List<string>();
                foreach (var cell in row.EnumerateArray())
                {
                    if (cell.ValueKind == JsonValueKind.String) cells.Add((cell.GetString() ?? "").Replace("|", "\\|"));
                    else if (cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty("content", out var cc))
                        cells.Add((cc.GetString() ?? "").Replace("|", "\\|"));
                    else cells.Add("");
                }
                sb.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderCodeToLml(JsonElement content, string? label)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var lang = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var attrs = BuildAttrs(
            ("lang", string.IsNullOrEmpty(lang) ? null : lang),
            ("caption", string.IsNullOrEmpty(caption) ? null : caption),
            ("label", label));
        return $"@code{attrs}\n{code}";
    }

    private static string RenderListToLml(JsonElement content)
    {
        var ordered = content.TryGetProperty("ordered", out var o) && o.ValueKind == JsonValueKind.True;
        var start = content.TryGetProperty("start", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 1;
        var attrs = ordered ? "[ordered=true]" : "";

        var sb = new StringBuilder();
        sb.AppendLine($"@list{attrs}");

        if (content.TryGetProperty("richItems", out var rich) && rich.ValueKind == JsonValueKind.Array && rich.GetArrayLength() > 0)
        {
            AppendRichListItemsLml(sb, rich, ordered, 0, start);
        }
        else if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in items.EnumerateArray())
            {
                var text = item.GetString() ?? "";
                var marker = ordered ? $"{start + idx}." : "-";
                sb.AppendLine($"{marker} {text}");
                idx++;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendRichListItemsLml(StringBuilder sb, JsonElement items, bool ordered, int depth, int start)
    {
        var indent = new string(' ', depth * 2);
        var idx = 0;
        foreach (var item in items.EnumerateArray())
        {
            var text = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var marker = ordered ? $"{start + idx}." : "-";
            sb.AppendLine($"{indent}{marker} {text}");
            if (item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
            {
                AppendRichListItemsLml(sb, children, ordered, depth + 1, 1);
            }
            idx++;
        }
    }

    private static string RenderBlockquoteToLml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var attribution = content.TryGetProperty("attribution", out var a) ? a.GetString() ?? "" : "";
        var attrs = BuildAttrs(("attribution", string.IsNullOrEmpty(attribution) ? null : attribution));
        return $"@blockquote{attrs}\n{text}";
    }

    private static string RenderTheoremToLml(JsonElement content, string? label)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var theoremType = content.TryGetProperty("theoremType", out var tt) ? tt.GetString() ?? "theorem" : "theorem";
        var title = content.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var attrs = BuildAttrs(
            ("type", theoremType),
            ("title", string.IsNullOrEmpty(title) ? null : title),
            ("label", label));
        return $"@theorem{attrs}\n{text}";
    }

    private static string RenderAbstractToLml(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"@abstract\n{text}";
    }

    private static string RenderEmbedToLml(JsonElement content, string? label)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var engine = content.TryGetProperty("engine", out var e) ? e.GetString() ?? "latex" : "latex";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var attrs = BuildAttrs(
            ("engine", engine),
            ("caption", string.IsNullOrEmpty(caption) ? null : caption),
            ("label", label));
        return $"@embed{attrs}\n{code}";
    }

    private static string RenderAlgorithmToLml(JsonElement content, string? label)
    {
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";
        var code = content.TryGetProperty("code", out var cd) ? cd.GetString() ?? "" : "";
        var attrs = BuildAttrs(
            ("title", string.IsNullOrEmpty(title) ? null : title),
            ("caption", string.IsNullOrEmpty(caption) ? null : caption),
            ("label", label));
        return $"@algorithm{attrs}\n{code}";
    }

    private static string RenderCalloutToLml(JsonElement content, string? label)
    {
        var type = content.TryGetProperty("calloutType", out var ct) ? ct.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var attrs = BuildAttrs(
            ("type", type),
            ("title", string.IsNullOrEmpty(title) ? null : title),
            ("label", label));
        return $"@alert{attrs}\n{text}";
    }

    private static string RenderFootnoteToLml(JsonElement content, string? label)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var attrs = BuildAttrs(("label", label));
        return $"@footnote{attrs}\n{text}";
    }

    private static string RenderBibliographyEntriesToLml(List<BibliographyEntry> entries)
    {
        if (entries.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("@bibliography");
        foreach (var entry in entries)
        {
            sb.Append(entry.CiteKey).Append(" = ").AppendLine(string.IsNullOrEmpty(entry.EntryType) ? "article" : entry.EntryType);
            var data = entry.Data.RootElement;
            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in data.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString() ?? "";
                        sb.Append("  ").Append(prop.Name).Append(" = ").AppendLine(val);
                    }
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderColumnLayoutToLml(JsonElement content)
    {
        var mode = content.TryGetProperty("mode", out var m) ? m.GetString() ?? "start" : "start";
        if (string.Equals(mode, "end", StringComparison.OrdinalIgnoreCase))
            return "@end-columnlayout";
        var columns = content.TryGetProperty("columns", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 2;
        columns = Math.Clamp(columns, 1, 3);
        return $"@columnlayout[columns={columns}]";
    }

    private static string? ReadLmlLabel(JsonElement content)
    {
        return content.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString()
            : null;
    }

    // Build "[key=val][key2=val2]" attribute string; skips nulls/empties.
    private static string BuildAttrs(params (string Key, string? Value)[] pairs)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var escaped = value.Replace("]", "\\]");
            sb.Append('[').Append(key).Append('=').Append(escaped).Append(']');
        }
        return sb.ToString();
    }
}
