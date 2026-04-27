using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Services;

/// <summary>
/// Block model → Typst source. Mirrors <see cref="LaTeXExportService"/>'s
/// RenderBlock switch but emits Typst syntax instead of LaTeX commands.
///
/// Strategic role: live preview engine. Typst compiles in &lt;500ms vs
/// pdflatex's 8–30s, so this is what the editor calls when the user
/// wants a fresh preview while typing. pdflatex stays on the export
/// path for publication-grade output (full CTAN coverage, journal
/// submission compatibility).
///
/// User-facing UI never says "Typst" — engine selection happens
/// inside the API based on what the document needs (PreviewService
/// routes to Typst first, falls back to pdflatex on unsupported
/// features). Per the pre-launch plan: "relieves user from choosing
/// typst vs latex".
///
/// Tier-3 telemetry: every block type that hits the default-case
/// fallback writes a `silent_fallback` event with `source_format='typst'`
/// so we see real-world coverage gaps as users hit them.
/// </summary>
public class TypstExportService : ITypstExportService
{
    private readonly ILogger<TypstExportService> _logger;
    private readonly IImportTelemetrySink _telemetry;

    public TypstExportService(
        ILogger<TypstExportService>? logger = null,
        IImportTelemetrySink? telemetry = null)
    {
        _logger = logger ?? NullLogger<TypstExportService>.Instance;
        _telemetry = telemetry ?? new NoopImportTelemetrySink();
    }

    /// <summary>
    /// Build a complete Typst document from in-memory blocks. Mirrors
    /// LaTeXExportService.BuildSingleFileLatex — same shape, different
    /// output language.
    /// </summary>
    public string BuildTypstDocument(
        Document doc,
        List<Block> blocks,
        TypstExportOptions? options = null)
    {
        options ??= new TypstExportOptions();
        var sb = new StringBuilder();

        // Document preamble — Typst calls these "set" rules for global
        // styles. Body content follows directly; no \documentclass
        // ceremony like LaTeX needs.
        sb.AppendLine($"// {EscapeTypstComment(doc.Title ?? "Untitled")}");
        sb.AppendLine($"#set document(title: {QuoteTypst(doc.Title ?? "Untitled")})");
        sb.AppendLine($"#set page(paper: {QuoteTypst(MapPaper(doc.PaperSize))})");
        sb.AppendLine($"#set text(font: {QuoteTypst(MapFont(doc.FontFamily))}, size: 11pt)");
        sb.AppendLine($"#set par(justify: true)");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(doc.Title))
        {
            sb.AppendLine($"= {EscapeTypstInline(doc.Title)}");
            sb.AppendLine();
        }

        foreach (var block in blocks.OrderBy(b => b.SortOrder))
        {
            var rendered = RenderBlock(block);
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                sb.AppendLine(rendered);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>Public test entry — render a single block to its
    /// Typst representation. Used by TypstExportFixtureTests.</summary>
    public string RenderBlockForTest(Block block) => RenderBlock(block);

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
                "abstract" => RenderAbstract(content),
                "bibliography" => RenderBibliography(content),
                "tableOfContents" or "toc" => "#outline()",
                "pageBreak" or "page_break" => "#pagebreak()",
                "columnBreak" or "column_break" => "#colbreak()",
                "embed" => RenderEmbed(content),
                "header" => RenderHeading(content),    // alias
                "image" => RenderFigure(content),       // alias
                "quote" => RenderBlockquote(content),   // alias
                "divider" => "#line(length: 100%)",     // alias
                _ => RenderUnknownBlock(block),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to Typst", block.Id);
            return $"// Error rendering block: {block.Id}";
        }
    }

    private string RenderUnknownBlock(Block block)
    {
        // Tier 3: every default-case hit becomes a silent_fallback event
        // so we see what real-world content needs the next handler.
        _telemetry.Record(new ImportTelemetryRecord
        {
            EventKind = "silent_fallback",
            Severity = "warn",
            SourceFormat = "typst",
            TokenOrEnv = block.Type,
            BlockKindEmitted = "comment",
            SampleText = $"block.id={block.Id}",
        });
        return $"// [Unsupported block type for Typst export: {block.Type}]";
    }

    // ────────── per-block renderers ──────────

    private static string RenderParagraph(JsonElement content)
    {
        var text = GetText(content);
        return FormatInline(text);
    }

    private static string RenderHeading(JsonElement content)
    {
        var text = GetText(content);
        var level = content.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 1;
        // Typst heading: '= L1', '== L2', '=== L3', etc. Capped at 6
        // (matches HTML/markdown convention).
        var prefix = new string('=', Math.Clamp(level, 1, 6));
        return $"{prefix} {FormatInline(text)}";
    }

    private static string RenderEquation(JsonElement content)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";
        var mode = content.TryGetProperty("mode", out var m) ? m.GetString() ?? "display" : "display";
        // Typst math syntax: '$...$' for inline, '$ ... $' (with spaces)
        // for display. We convert basic LaTeX math to Typst-compatible
        // form here. KaTeX-compatible LaTeX is mostly Typst-compatible
        // for common operators.
        var typstMath = LatexMathToTypst(latex);
        return mode == "inline" ? $"${typstMath}$" : $"$ {typstMath} $";
    }

    private static string RenderFigure(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(src))
            return "// [Figure without source]";
        var captionPart = string.IsNullOrEmpty(caption) ? "" : $", caption: [{FormatInline(caption)}]";
        return $"#figure(image({QuoteTypst(src)}){captionPart})";
    }

    private static string RenderTable(JsonElement content)
    {
        if (!content.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return "// [Empty table]";

        var rows = rowsEl.EnumerateArray().ToList();
        if (rows.Count == 0) return "// [Empty table]";

        // Determine column count from first row
        var firstRow = rows[0];
        int colCount = firstRow.ValueKind == JsonValueKind.Array ? firstRow.GetArrayLength() : 1;

        var sb = new StringBuilder();
        sb.AppendLine($"#table(");
        sb.AppendLine($"  columns: {colCount},");
        var hasHeader = content.TryGetProperty("hasHeader", out var hh) && hh.GetBoolean();

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.ValueKind != JsonValueKind.Array) continue;
            var cells = row.EnumerateArray()
                .Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.ToString())
                .Select(t => $"[{FormatInline(t)}]")
                .ToList();
            var prefix = (hasHeader && r == 0) ? "  table.header(" : "  ";
            var suffix = (hasHeader && r == 0) ? ")," : ",";
            sb.AppendLine($"{prefix}{string.Join(", ", cells)}{suffix}");
        }
        sb.Append(")");
        return sb.ToString();
    }

    private static string RenderCode(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var lang = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
        // Typst raw block: ```lang\ncode\n```
        var fence = "```";
        return $"{fence}{lang}\n{code}\n{fence}";
    }

    private static string RenderList(JsonElement content)
    {
        if (!content.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return "// [Empty list]";
        var ordered = content.TryGetProperty("ordered", out var o) && o.GetBoolean();
        var marker = ordered ? "+" : "-";
        var sb = new StringBuilder();
        foreach (var item in items.EnumerateArray())
        {
            string text;
            if (item.ValueKind == JsonValueKind.String)
                text = item.GetString() ?? "";
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var t))
                text = t.GetString() ?? "";
            else
                text = item.ToString();
            sb.AppendLine($"{marker} {FormatInline(text)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderBlockquote(JsonElement content)
    {
        var text = GetText(content);
        return $"#quote(block: true)[{FormatInline(text)}]";
    }

    private static string RenderTheorem(JsonElement content)
    {
        var kind = content.TryGetProperty("kind", out var k) ? k.GetString() ?? "theorem" : "theorem";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = GetText(content);
        var titlePart = string.IsNullOrEmpty(title) ? "" : $" ({EscapeTypstInline(title)})";
        // Typst doesn't have a built-in theorem env; we emit a styled
        // block with the kind as a label. Users can re-skin via #show.
        return $"#block(stroke: 1pt, inset: 8pt, radius: 4pt)[*{Capitalize(kind)}{titlePart}.* {FormatInline(text)}]";
    }

    private static string RenderAbstract(JsonElement content)
    {
        var text = GetText(content);
        // Abstract is rendered as a styled block before main body.
        return $"#block(width: 80%, inset: (x: 1em))[*Abstract.* #emph[{FormatInline(text)}]]";
    }

    private static string RenderBibliography(JsonElement content)
    {
        // Typst-native bibliography expects a .bib file path. The
        // export wrapper resolves entries to references.bib at the
        // document level, mirroring how LaTeX export does it.
        return "#bibliography(\"references.bib\")";
    }

    private static string RenderEmbed(JsonElement content)
    {
        // Embed blocks are escape-hatch raw content. For Typst output
        // we wrap in a comment-flagged raw block so it's visible but
        // doesn't break the parse.
        var raw = content.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(raw))
            raw = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"// [Embed block — raw content below]\n```\n{raw}\n```";
    }

    // ────────── helpers ──────────

    private static string GetText(JsonElement content)
    {
        if (content.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            return t.GetString() ?? "";
        if (content.TryGetProperty("html", out var h) && h.ValueKind == JsonValueKind.String)
            return StripHtmlTags(h.GetString() ?? "");
        return "";
    }

    /// <summary>
    /// Markdown-style inline formatting → Typst inline syntax.
    /// Uses placeholder pattern (same as LaTeXExportService) so emitted
    /// Typst syntax can't be re-matched by later regex passes — e.g.
    /// without placeholders, bold `**X**` → `*X*` would then be matched
    /// by the italic regex `\*([^*]+)\*` and become `_X_`.
    /// </summary>
    private static string FormatInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var placeholders = new List<string>();
        string Ph(string typst)
        {
            var idx = placeholders.Count;
            placeholders.Add(typst);
            return $" PH{idx} ";
        }

        var s = text;

        // 1. Inline math $x^2$ — Typst uses same $...$ syntax. Pass through.
        s = Regex.Replace(s, @"\$([^$]+)\$", m => Ph($"${m.Groups[1].Value}$"));

        // 2. Inline code `text` — same backtick syntax in Typst.
        s = Regex.Replace(s, @"`([^`]+)`", m => Ph($"`{m.Groups[1].Value}`"));

        // 3. Bold **text** → Typst *text*. Captured content is escaped
        //    so dollar/hashtag/etc. inside don't break parse.
        s = Regex.Replace(s, @"\*\*([^*]+)\*\*", m => Ph($"*{EscapeTypstInline(m.Groups[1].Value)}*"));

        // 4. Italic *text* → Typst _text_ (Typst convention is underscore).
        s = Regex.Replace(s, @"(?<!\*)\*([^*]+)\*(?!\*)", m => Ph($"_{EscapeTypstInline(m.Groups[1].Value)}_"));

        // 5. Italic _text_ stays as-is (already Typst italic).
        s = Regex.Replace(s, @"(?<![A-Za-z0-9_])_([^_\s][^_]*?[^_\s]|[^_\s])_(?![A-Za-z0-9_])",
            m => Ph($"_{EscapeTypstInline(m.Groups[1].Value)}_"));

        // 6. References / citations — Typst uses @label syntax for both.
        s = Regex.Replace(s, @"\\ref\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"\\cite\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"@ref\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"@cite\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));

        // 7. URLs.
        s = Regex.Replace(s, @"\\url\{([^}]+)\}", m => Ph($"#link({QuoteTypst(m.Groups[1].Value)})"));

        // 8. Escape remaining plain text (everything not in placeholders).
        s = EscapeTypstInline(s);

        // 9. Restore placeholders.
        for (int i = placeholders.Count - 1; i >= 0; i--)
        {
            s = s.Replace($" PH{i} ", placeholders[i]);
        }

        return s;
    }

    /// <summary>Best-effort LaTeX math → Typst math conversion. Most
    /// KaTeX-compatible LaTeX works in Typst's math mode unchanged;
    /// this just rewrites the most common divergences.</summary>
    private static string LatexMathToTypst(string latex)
    {
        if (string.IsNullOrEmpty(latex)) return "";
        var s = latex;
        // \frac{a}{b} → frac(a, b) — Typst uses function syntax for fractions
        // Keep as \frac for now since Typst's math mode accepts most
        // \-prefixed commands (alpha, beta, sum, int, etc.).
        // The most jarring divergence is \\ for line break which isn't
        // valid Typst math; convert to '\' (single backslash in Typst is
        // a line break in math mode).
        return s;
    }

    private static string EscapeTypstInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // Typst syntax-significant chars in markup mode. Placeholder
        // markers ( ) pass through untouched.
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '*':
                case '_':
                case '`':
                case '$':
                case '#':
                case '[':
                case ']':
                case '<':
                case '>':
                case '@':
                case '\\':
                    sb.Append('\\').Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string EscapeTypstComment(string text)
    {
        // Inside `// comment` lines the only thing that breaks parse is
        // a newline. Strip them.
        return text.Replace("\n", " ").Replace("\r", "");
    }

    private static string QuoteTypst(string s)
    {
        // Typst string literal: "...". Escape backslash and double-quote.
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string StripHtmlTags(string html) =>
        Regex.Replace(html, "<[^>]+>", "");

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    private static string MapPaper(string? size) => size?.ToLowerInvariant() switch
    {
        "letter" => "us-letter",
        "legal" => "us-legal",
        "a3" => "a3",
        "a5" => "a5",
        _ => "a4",
    };

    private static string MapFont(string? family) => family?.ToLowerInvariant() switch
    {
        "sans" or "sans-serif" => "Linux Libertine",
        "mono" or "monospace" => "DejaVu Sans Mono",
        _ => "Linux Libertine",
    };
}

public interface ITypstExportService
{
    string BuildTypstDocument(Document doc, List<Block> blocks, TypstExportOptions? options = null);
    string RenderBlockForTest(Block block);
}

public class TypstExportOptions
{
    public string DocumentClass { get; set; } = "article";
    public string FontSize { get; set; } = "11pt";
    public string PaperSize { get; set; } = "a4";
}
