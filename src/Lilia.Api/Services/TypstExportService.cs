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
    ///
    /// Column handling (LILIA-136): per-block "effective column count"
    /// is derived from layout-dimension groups (see <see cref="BlockGroup"/>).
    /// Blocks NOT in any layout group fall back to <c>doc.Columns</c>
    /// (the doc-level default we shipped in LILIA-135). Contiguous
    /// blocks with the same effective count are batched into a single
    /// <c>#columns(N)[ ... ]</c> wrapper; runs of 1 column are emitted
    /// inline. This composes naturally — a 2-col doc with a 1-col
    /// layout group around the abstract gets a single-column abstract
    /// followed by a `#columns(2)[...]` body, matching the standard
    /// academic-paper shape.
    /// </summary>
    public string BuildTypstDocument(
        Document doc,
        List<Block> blocks,
        IReadOnlyList<BlockGroup>? layoutGroups = null,
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
        // Font fallback list — Typst tries each in order. "New Computer
        // Modern" ships bundled with the typst binary so compile never
        // fails on a missing system font; production servers also have
        // Linux Libertine installed via the Dockerfile.
        sb.AppendLine($"#set text(font: ({MapFontList(doc.FontFamily)}), size: 11pt)");
        sb.AppendLine($"#set par(justify: true)");
        sb.AppendLine();

        // Title heading — emit `= Title` so Typst has a visible title.
        // BUT: imported documents (LaTeX, DOCX) often promote \title{X}
        // into a top-level heading block whose text matches the doc
        // title. If we always emit our own `= Title` AND the matching
        // heading block also renders, the user sees the title twice.
        // Skip the duplicate block via FirstHeadingMatchesTitle below.
        var titleDuplicateBlockId = FindTitleDuplicateBlockId(doc.Title, blocks);
        if (!string.IsNullOrEmpty(doc.Title))
        {
            sb.AppendLine($"= {EscapeTypstInline(doc.Title)}");
            sb.AppendLine();
        }

        var defaultCols = Math.Clamp(doc.Columns, 1, 3);
        var blockToCols = BuildBlockColumnMap(layoutGroups, defaultCols);

        // Emit blocks in run-batches of equal effective column count.
        // Switching column count flushes the current run wrapper and
        // opens a new one. Single-column runs are emitted inline (no
        // wrapper) since `#columns(1)[...]` is a no-op that just adds
        // visual noise to the source.
        var orderedBlocks = blocks
            .Where(b => !titleDuplicateBlockId.HasValue || b.Id != titleDuplicateBlockId.Value)
            .OrderBy(b => b.SortOrder)
            .ToList();

        int? currentRunCols = null;
        foreach (var block in orderedBlocks)
        {
            var rendered = RenderBlock(block);
            if (string.IsNullOrWhiteSpace(rendered)) continue;

            var blockCols = blockToCols.TryGetValue(block.Id, out var n) ? n : defaultCols;

            if (blockCols != currentRunCols)
            {
                if (currentRunCols is > 1) sb.AppendLine("]");
                if (blockCols > 1) sb.AppendLine($"#columns({blockCols})[");
                currentRunCols = blockCols;
            }

            sb.AppendLine(rendered);
            sb.AppendLine();
        }

        if (currentRunCols is > 1) sb.AppendLine("]");

        return sb.ToString();
    }

    /// <summary>
    /// Map each block id to its effective column count. Layout-dimension
    /// groups override the document-level default. Blocks not in any
    /// layout group are absent from the map (callers fall back to default).
    /// </summary>
    private static Dictionary<Guid, int> BuildBlockColumnMap(
        IReadOnlyList<BlockGroup>? layoutGroups,
        int defaultCols)
    {
        var map = new Dictionary<Guid, int>();
        if (layoutGroups == null) return map;

        foreach (var group in layoutGroups)
        {
            if (group.Dimension != BlockGroupDimensions.Layout) continue;

            int cols = defaultCols;
            if (group.Attributes.RootElement.TryGetProperty("columns", out var colsProp)
                && colsProp.ValueKind == JsonValueKind.Number
                && colsProp.TryGetInt32(out var raw))
            {
                cols = Math.Clamp(raw, 1, 3);
            }

            foreach (var membership in group.Memberships)
            {
                map[membership.BlockId] = cols;
            }
        }

        return map;
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
                // Match both the canonical camelCase (BlockTypes constants)
                // and the all-lowercase forms that legacy / imported data
                // stores. Same casing tolerance the LaTeX path applies.
                "tableOfContents" or "tableofcontents" or "toc" => "#outline()",
                "pageBreak" or "pagebreak" or "page_break" => "#pagebreak()",
                "columnBreak" or "columnbreak" or "column_break" => "#colbreak()",
                "embed" => RenderEmbed(content),
                "callout" => RenderCallout(content),
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
        // Strip baked-in numbering prefix ("1. ", "1.1 ", "I. ", "A. ")
        // — legacy imports stored the section number inside the heading
        // text. With Typst auto-numbering on (and TOC entries deriving
        // from heading text), leaving the prefix in shows the number
        // twice in the rendered PDF.
        return $"{prefix} {FormatInline(StripBakedNumberingPrefix(text))}";
    }

    /// <summary>
    /// Mirror of <c>SectionKeywordRegistry.StripNumberingPrefix</c> from
    /// the import path. Catches "1. ", "1.1 ", "1.1.1 ", "I. ", "II. ",
    /// "A. " — leaves anything not numeric/roman/alpha-prefix alone.
    /// </summary>
    private static string StripBakedNumberingPrefix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var match = Regex.Match(text, @"^(?:\d+(?:\.\d+)*\.?\s+|[IVXLC]+\.\s+|[A-Z]\.\s+)(.+)$");
        return match.Success ? match.Groups[1].Value : text;
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

        // Figures with external / placeholder URLs can't be resolved
        // inside Typst's sandboxed file system at compile time. Render
        // a drawn placeholder rect instead so the rest of the document
        // still compiles via the fast preview path. The LaTeX export
        // path resolves the real asset on the export path.
        if (IsUnresolvableFigureSrc(src))
        {
            // Typst rect with fill+stroke; placed inside #figure so
            // captions still render via the standard caption slot.
            var inner = "rect(width: 80%, height: 4cm, fill: rgb(\"#f3f4f6\"), stroke: rgb(\"#d1d5db\"), inset: 1em, radius: 4pt)[#align(center + horizon)[#text(fill: rgb(\"#9ca3af\"))[Image placeholder]]]";
            return $"#figure({inner}{captionPart})";
        }

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

        // `kind` (Phase 2) takes precedence; description lists use
        // Typst's native term-list syntax `/ term: description`.
        var kind = ResolveKind(content);
        if (kind == "description")
        {
            var dsb = new StringBuilder();
            AppendDescriptionItems(items, depth: 0, dsb);
            return dsb.ToString().TrimEnd();
        }

        var ordered = ResolveOrdered(content);
        var sb = new StringBuilder();

        // labelFormat + start are ordered-only. Map to Typst's
        // `#set enum(numbering: "...", start: N)`. We scope the rule
        // inside a content block `#[ ... ]` so it only affects this
        // list, mirroring how the LaTeX path scopes via enumitem options.
        var numberingPattern = ordered ? MapLabelFormatToTypstNumbering(content) : null;
        var startNum = ordered ? ResolveStart(content) : 1;
        var needsWrap = numberingPattern != null || startNum != 1;

        if (needsWrap)
        {
            sb.AppendLine("#[");
            var setArgs = new List<string>();
            if (numberingPattern != null) setArgs.Add($"numbering: \"{numberingPattern}\"");
            if (startNum != 1) setArgs.Add($"start: {startNum}");
            sb.AppendLine($"  #set enum({string.Join(", ", setArgs)})");
            AppendListItems(items, ordered, depth: 1, sb);
            sb.AppendLine("]");
        }
        else
        {
            AppendListItems(items, ordered, depth: 0, sb);
        }

        return sb.ToString().TrimEnd();
    }

    private static string? ResolveKind(JsonElement content)
    {
        if (content.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
            return k.GetString();
        return null;
    }

    /// <summary>
    /// Description-list (term-list) emitter. Each item becomes a
    /// `/ term: description` line, which is Typst's native syntax for
    /// a definition list. Nested children inherit description kind.
    /// </summary>
    private static void AppendDescriptionItems(JsonElement items, int depth, StringBuilder sb)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        var indent = new string(' ', depth * 2);
        foreach (var item in items.EnumerateArray())
        {
            string term = "";
            string description = "";
            JsonElement? children = null;
            if (item.ValueKind == JsonValueKind.String)
            {
                term = item.GetString() ?? "";
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    term = t.GetString() ?? "";
                if (item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                    description = d.GetString() ?? "";
                if (item.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array && ch.GetArrayLength() > 0)
                    children = ch;
            }
            sb.AppendLine($"{indent}/ {FormatInline(term)}: {FormatInline(description)}");
            if (children.HasValue)
                AppendDescriptionItems(children.Value, depth + 1, sb);
        }
    }

    private static string? MapLabelFormatToTypstNumbering(JsonElement content)
    {
        if (!content.TryGetProperty("labelFormat", out var lf) || lf.ValueKind != JsonValueKind.String)
            return null;
        // Match the LaTeX path's `(\alph*)` style so the same doc looks
        // the same in PDF (LaTeX) and Typst.
        return lf.GetString() switch
        {
            "alpha" => "(a)",
            "Alpha" => "(A)",
            "roman" => "(i)",
            "Roman" => "(I)",
            _ => null, // "number" → Typst default ("1.")
        };
    }

    private static int ResolveStart(JsonElement content)
    {
        if (content.TryGetProperty("start", out var s) && s.TryGetInt32(out var n) && n >= 1)
            return n;
        return 1;
    }

    /// <summary>
    /// Walks the items array recursively. Each level adds two spaces of
    /// indentation — Typst's native syntax for nested lists. Item shape
    /// matches what the editor's RenderService.RenderListItemToHtml
    /// already accepts: string OR { text|richText, children?: [...] }.
    /// </summary>
    private static void AppendListItems(JsonElement items, bool ordered, int depth, StringBuilder sb)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        var marker = ordered ? "+" : "-";
        var indent = new string(' ', depth * 2);
        foreach (var item in items.EnumerateArray())
        {
            string text;
            JsonElement? children = null;
            if (item.ValueKind == JsonValueKind.String)
            {
                text = item.GetString() ?? "";
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    text = t.GetString() ?? "";
                }
                else if (item.TryGetProperty("richText", out var rt) && rt.ValueKind == JsonValueKind.Array)
                {
                    var rsb = new StringBuilder();
                    foreach (var span in rt.EnumerateArray())
                    {
                        if (span.TryGetProperty("text", out var st) && st.ValueKind == JsonValueKind.String)
                            rsb.Append(st.GetString());
                    }
                    text = rsb.ToString();
                }
                else
                {
                    text = "";
                }
                if (item.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array && ch.GetArrayLength() > 0)
                    children = ch;
            }
            else
            {
                text = item.ToString();
            }

            sb.AppendLine($"{indent}{marker} {FormatInline(text)}");
            if (children.HasValue)
            {
                // Nested list inherits parent's ordered/unordered. The
                // editor model doesn't currently let users mix ordered
                // and unordered within the same tree, mirroring the
                // HTML renderer's inheritance.
                AppendListItems(children.Value, ordered, depth + 1, sb);
            }
        }
    }

    private static bool ResolveOrdered(JsonElement content)
    {
        if (content.TryGetProperty("listType", out var lt) && lt.ValueKind == JsonValueKind.String)
            return lt.GetString() == "ordered";
        if (content.TryGetProperty("ordered", out var o) && o.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }

    private static string RenderCallout(JsonElement content)
    {
        // Mirror of the LaTeX RenderCalloutToLatex tcolorbox output —
        // Typst's #block(fill: ..., stroke: ...) is the closest visual
        // equivalent. Free-form `color` attr overrides the variant
        // default. We tint with `.lighten(85%)` for the background and
        // use the raw color for the left stroke.
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = GetText(content);
        var rawColor = content.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "";
        // Color resolution: free-form name → Typst rgb hex; empty falls
        // back to the variant's default (note=blue, tip=green, …).
        var typstColor = !string.IsNullOrEmpty(rawColor)
            ? MapColorToTypst(rawColor)
            : variant switch
            {
                "tip" => "rgb(\"#16a34a\")",
                "warning" => "rgb(\"#d97706\")",
                "important" => "rgb(\"#dc2626\")",
                "example" => "rgb(\"#9333ea\")",
                _ => "rgb(\"#2563eb\")",
            };
        var displayTitle = !string.IsNullOrEmpty(title) ? title : char.ToUpper(variant[0]) + variant[1..];
        var titleQ = QuoteTypst(displayTitle);
        var body = FormatInline(text);
        return $"#block(stroke: (left: 3pt + {typstColor}), inset: (left: 12pt, y: 8pt), spacing: 0.8em)[*{titleQ}* \\\n{body}]";
    }

    private static string RenderBlockquote(JsonElement content)
    {
        var text = GetText(content);
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "simple" : "simple";
        var attribution = content.TryGetProperty("attribution", out var a) ? a.GetString() ?? "" : "";

        // Typst's #quote() takes an optional `attribution` parameter that
        // shows beneath the block — perfect mirror for the LaTeX
        // `\epigraph{text}{source}` form. Verse keeps the line-break
        // semantics via `#linebreak()` between lines.
        switch (variant)
        {
            case "epigraph":
            {
                var escapedAttr = QuoteTypst(attribution);
                return $"#quote(block: true, attribution: {escapedAttr})[{FormatInline(text)}]";
            }
            case "verse":
            {
                var lines = (text ?? "").Split('\n');
                var body = string.Join(" #linebreak() ", lines.Select(FormatInline));
                return $"#block(spacing: 0.6em)[{body}]";
            }
            default:
                return $"#quote(block: true)[{FormatInline(text)}]";
        }
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

        // 1a. Display math $$x^2$$ — convert to Typst's `$ X $` form
        //     (Typst doesn't have a separate display-math syntax;
        //     spaces inside $...$ enable display layout). MUST run
        //     before the single-$ rule so the outer pair isn't read
        //     as two adjacent inline spans.
        s = Regex.Replace(s, @"\$\$([^$]+)\$\$", m => Ph($"$ {LatexMathToTypst(m.Groups[1].Value)} $"));

        // 1b. Inline math $x^2$ — Typst uses same $...$ syntax, BUT
        //     LaTeX math commands ($\mathbb{R}$, $\int$ etc.) need
        //     translation. Otherwise an inline math literal inside a
        //     paragraph/theorem/abstract breaks the whole-document
        //     Typst compile and forces a silent_fallback to pdflatex.
        s = Regex.Replace(s, @"\$([^$]+)\$", m => Ph($"${LatexMathToTypst(m.Groups[1].Value)}$"));

        // 1c. Comment marker `[%…%]` — the editor's "comment out" toggle
        //     serialises to this shape (paired with the LaTeX `\iffalse`
        //     conversion in RenderService.ProcessLatexText). Typst hides
        //     content from output via `#if false [ ... ]` — works inline
        //     AND block, no package needed. Placeholder-protect so the
        //     escape pass below doesn't backslash the brackets.
        s = Regex.Replace(s, @"\[%([\s\S]+?)%\]",
            m => Ph($"#if false [{EscapeTypstInline(m.Groups[1].Value)}]"));

        // 1d. Smallcaps `^^…^^`, subscript `%%…%%`, superscript `^…^` —
        //     match the LML markers the editor serialises. Smallcaps
        //     MUST run before single-caret sup so `^^X^^` isn't grabbed
        //     by the sup regex.
        s = Regex.Replace(s, @"\^\^([^^]+)\^\^",
            m => Ph($"#smallcaps[{EscapeTypstInline(m.Groups[1].Value)}]"));
        s = Regex.Replace(s, @"%%([^%]+)%%",
            m => Ph($"#sub[{EscapeTypstInline(m.Groups[1].Value)}]"));
        s = Regex.Replace(s, @"(?<!\^)\^([^^\s][^^]*?)\^(?!\^)",
            m => Ph($"#super[{EscapeTypstInline(m.Groups[1].Value)}]"));

        // 1e. LaTeX inline-styling commands the editor serialises directly
        //     (font size group, color, highlight). These came from the
        //     2026-05-13 ribbon work — TipTap marks for textColor /
        //     fontSize / colored highlight serialise to LaTeX commands
        //     (no markdown equivalent). The LaTeX preview already
        //     understands them via ProcessLatexText; Typst needs its own
        //     translation so the live PDF preview shows the same styling
        //     as the source pane.
        //
        //     Color name maps below are the same 24-color palette the
        //     ribbon picker exposes, mirrored to Typst's named colors
        //     where they exist + RGB hex elsewhere. Unknown names pass
        //     through to Typst as-is (most CSS color names work).
        s = Regex.Replace(s, @"\\textcolor\{([^{}]+)\}\{([^{}]+)\}",
            m =>
            {
                var color = MapColorToTypst(m.Groups[1].Value);
                var content = EscapeTypstInline(m.Groups[2].Value);
                return Ph($"#text(fill: {color})[{content}]");
            });
        // `\hl{text}` / `\hl[color]{text}` — soul package highlight on
        // the LaTeX side, Typst's #highlight on this side. Default yellow
        // when no color provided.
        s = Regex.Replace(s, @"\\hl(?:\[([^\]]+)\])?\{([^{}]+)\}",
            m =>
            {
                var raw = m.Groups[1].Success ? m.Groups[1].Value : "yellow";
                var color = MapHighlightColorToTypst(raw);
                var content = EscapeTypstInline(m.Groups[2].Value);
                return Ph($"#highlight(fill: {color})[{content}]");
            });
        // `{\large text}` / `{\Large text}` group form — LaTeX size
        // switches the local group's font size. Map the canonical 10
        // size names to Typst em scales matching ProcessLatexText's
        // SIZE_EM table in render-markers.ts.
        s = Regex.Replace(s,
            @"\{\\(tiny|scriptsize|footnotesize|small|normalsize|large|Large|LARGE|huge|Huge)\s+([^{}]+)\}",
            m =>
            {
                var em = MapLatexSizeToEm(m.Groups[1].Value);
                var content = EscapeTypstInline(m.Groups[2].Value);
                return Ph($"#text(size: {em}em)[{content}]");
            });

        // 2. Inline code `text` — same backtick syntax in Typst.
        s = Regex.Replace(s, @"`([^`]+)`", m => Ph($"`{m.Groups[1].Value}`"));

        // 2.5. LaTeX literal pass — first-N high-frequency commands the
        //      parser passes through unchanged from imported documents
        //      (CV, journal article paste, etc.). Driven by post-launch
        //      silent_fallback telemetry: top gap was \\noindent /
        //      \\hfill / \\\\ pass-through hitting Typst's parser.
        //      Strategy: rewrite into the markdown / Typst surface that
        //      later steps already understand, so each command needs
        //      one regex and no extra placeholder bookkeeping.
        s = TranslateLatexLiterals(s, Ph);

        // 3. Bold **text** → Typst *text*. Captured content is escaped
        //    so dollar/hashtag/etc. inside don't break parse.
        s = Regex.Replace(s, @"\*\*([^*]+)\*\*", m => Ph($"*{EscapeTypstInline(m.Groups[1].Value)}*"));

        // 4. Italic *text* → Typst _text_ (Typst convention is underscore).
        s = Regex.Replace(s, @"(?<!\*)\*([^*]+)\*(?!\*)", m => Ph($"_{EscapeTypstInline(m.Groups[1].Value)}_"));

        // 5. Italic _text_ stays as-is (already Typst italic).
        s = Regex.Replace(s, @"(?<![A-Za-z0-9_])_([^_\s][^_]*?[^_\s]|[^_\s])_(?![A-Za-z0-9_])",
            m => Ph($"_{EscapeTypstInline(m.Groups[1].Value)}_"));

        // 6. References / citations — Typst uses @label syntax for both.
        s = Regex.Replace(s, @"\\eqref\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"\\ref\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"\\cite\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"@ref\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));
        s = Regex.Replace(s, @"@cite\{([^}]+)\}", m => Ph($"@{m.Groups[1].Value}"));

        // 7. URLs + links.
        s = Regex.Replace(s, @"\\url\{([^}]+)\}", m => Ph($"#link({QuoteTypst(m.Groups[1].Value)})"));
        // Two-arg \href{url}{text} first, then single-arg \href{url} as a
        // bare link (covers users who typed only the URL).
        s = Regex.Replace(s, @"\\href\{([^}]+)\}\{([^}]+)\}",
            m => Ph($"#link({QuoteTypst(m.Groups[1].Value)})[{EscapeTypstInline(m.Groups[2].Value)}]"));
        s = Regex.Replace(s, @"\\href\{([^}]+)\}", m => Ph($"#link({QuoteTypst(m.Groups[1].Value)})"));

        // 7.5. \footnote{X} — Typst uses #footnote[X]
        s = Regex.Replace(s, @"\\footnote\{([^}]+)\}",
            m => Ph($"#footnote[{EscapeTypstInline(m.Groups[1].Value)}]"));

        // 7.6. \label{X} — Typst uses <X> after the heading/element.
        // For inline contexts we drop the label (Typst's label semantics
        // attach to preceding content; mid-paragraph labels rarely make
        // semantic sense). Keeps the source compiling.
        s = Regex.Replace(s, @"\\label\{([^}]+)\}", m => Ph($"<{m.Groups[1].Value}>"));

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

        // Top-N LaTeX math commands surfaced by silent_fallback telemetry.
        // Typst math mode accepts most LaTeX greek/operators as-is
        // (\alpha, \sum, \int, \in, \to, etc.) but its font-family
        // commands have a different syntax. Translate the families
        // first; arguments inside {...} pass through untouched.
        s = Regex.Replace(s, @"\\mathbb\{([^{}]+)\}", "bb($1)");
        s = Regex.Replace(s, @"\\mathcal\{([^{}]+)\}", "cal($1)");
        s = Regex.Replace(s, @"\\mathbf\{([^{}]+)\}", "bold($1)");
        s = Regex.Replace(s, @"\\mathit\{([^{}]+)\}", "italic($1)");
        s = Regex.Replace(s, @"\\mathrm\{([^{}]+)\}", "upright($1)");
        s = Regex.Replace(s, @"\\mathsf\{([^{}]+)\}", "sans($1)");
        s = Regex.Replace(s, @"\\mathtt\{([^{}]+)\}", "mono($1)");
        s = Regex.Replace(s, @"\\mathfrak\{([^{}]+)\}", "frak($1)");

        // \text{X} inside math mode → upright text. Typst spells this
        // as `"X"` (literal string in math).
        s = Regex.Replace(s, @"\\text\{([^{}]+)\}", "\"$1\"");

        // Matrix environments — must run BEFORE the line-break
        // translation below, because the matrix split relies on
        // the LaTeX `\\\\` row-separator surviving intact. Doing
        // the line-break pass first would turn it into `\ ` and
        // every matrix would render as a single row.
        s = ConvertMatrixEnvironments(s);

        // Spacing commands → Typst equivalents. Run before bareOps
        // strip so `\quad` doesn't end up half-stripped.
        s = Regex.Replace(s, @"\\quad(?![A-Za-z])", "quad");
        s = Regex.Replace(s, @"\\qquad(?![A-Za-z])", "wide");
        s = Regex.Replace(s, @"\\,(?![A-Za-z])", "thin");
        s = Regex.Replace(s, @"\\;(?![A-Za-z])", "med");
        s = Regex.Replace(s, @"\\:(?![A-Za-z])", "med");
        s = Regex.Replace(s, @"\\!(?![A-Za-z])", "");

        // \\ inside math is a line break in LaTeX. Typst math line
        // break is single backslash; remap to avoid the parser
        // treating it as escape.
        s = Regex.Replace(s, @"\\\\(?!\\)", "\\ ");

        // Big operators that need a name swap (LaTeX → Typst name).
        var operatorRenames = new (string From, string To)[]
        {
            ("int",    "integral"),
            ("iint",   "integral.double"),
            ("iiint",  "integral.triple"),
            ("oint",   "integral.cont"),
            ("prod",   "product"),
            ("coprod", "product.co"),
            ("lim",    "limits.lim"),
            ("limsup", "limits.lim.sup"),
            ("liminf", "limits.lim.inf"),
        };
        foreach (var (from, to) in operatorRenames)
        {
            // `\b` doesn't fire between letter and `_` (both word chars),
            // so use an explicit non-letter lookahead. End-of-string also
            // counts as a valid match boundary.
            s = Regex.Replace(s, $@"\\{from}(?![A-Za-z])", to);
        }

        // Functions / operators Typst math accepts as bare identifiers
        // — just strip the leading LaTeX backslash.
        var bareOps = new[]
        {
            "sum",
            "min", "max", "sup", "inf",
            "sin", "cos", "tan", "cot", "sec", "csc",
            "arcsin", "arccos", "arctan",
            "sinh", "cosh", "tanh",
            "log", "ln", "exp", "det", "dim", "ker", "deg",
            "gcd", "hom", "arg", "Pr", "mod",
        };
        foreach (var op in bareOps)
        {
            s = Regex.Replace(s, $@"\\{op}(?![A-Za-z])", op);
        }

        // \sqrt{x} → sqrt(x) ; \sqrt[n]{x} → root(n, x)
        // MUST run before \frac so a nested `\frac{1}{\sqrt{2}}` has
        // the inner braces resolved before \frac's `[^{}]+` test.
        s = Regex.Replace(s, @"\\sqrt\[([^\]]+)\]\{([^{}]+)\}", "root($1, $2)");
        s = Regex.Replace(s, @"\\sqrt\{([^{}]+)\}", "sqrt($1)");

        // \frac{a}{b} → frac(a, b) — Typst's function-call syntax.
        // Apply in a loop so nested `\frac{\frac{a}{b}}{c}` resolves
        // bottom-up (each pass eats the innermost match).
        for (int pass = 0; pass < 5; pass++)
        {
            var next = Regex.Replace(s, @"\\frac\{([^{}]+)\}\{([^{}]+)\}", "frac($1, $2)");
            if (next == s) break;
            s = next;
        }
        // (matrix env conversion moved earlier in the pipeline so it
        // runs before the `\\\\` line-break replacement.)

        // Greek letters — Typst math accepts these as bare identifiers
        // ("alpha", "beta", "Gamma", etc.). Strip the leading backslash.
        var greekLowercase = new[]
        {
            "alpha", "beta", "gamma", "delta", "epsilon", "varepsilon",
            "zeta", "eta", "theta", "vartheta", "iota", "kappa", "lambda",
            "mu", "nu", "xi", "pi", "varpi", "rho", "varrho", "sigma",
            "varsigma", "tau", "upsilon", "phi", "varphi", "chi", "psi",
            "omega",
        };
        var greekUppercase = new[]
        {
            "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma",
            "Upsilon", "Phi", "Psi", "Omega",
        };
        foreach (var g in greekLowercase.Concat(greekUppercase))
        {
            s = Regex.Replace(s, $@"\\{g}(?![A-Za-z])", g);
        }

        // _{X} / ^{X} → Typst's _(X) / ^(X) for multi-char sub/super
        // scripts. LaTeX requires braces; Typst uses parens for groups.
        // Single-char sub/super (`x_i`, `x^2`) work in both unchanged.
        s = Regex.Replace(s, @"_\{([^{}]+)\}", "_($1)");
        s = Regex.Replace(s, @"\^\{([^{}]+)\}", "^($1)");

        return s;
    }

    /// <summary>
    /// Find the first heading block whose text matches the document
    /// title (case-insensitive, trim, normalize whitespace). When such
    /// a block exists, callers skip rendering it so the title doesn't
    /// double up alongside the explicit `= Title` line. Returns null
    /// when there's no duplicate.
    /// </summary>
    private static Guid? FindTitleDuplicateBlockId(string? title, List<Block> blocks)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var canonTitle = NormalizeTitle(title);
        foreach (var block in blocks.OrderBy(b => b.SortOrder))
        {
            if (!string.Equals(block.Type, "heading", StringComparison.OrdinalIgnoreCase))
            {
                // Only a *leading* heading counts as a duplicate-title;
                // a heading further down is just the user's own H1.
                // Stop at the first non-heading content block.
                if (IsContentBlock(block.Type)) return null;
                continue;
            }
            try
            {
                var text = block.Content.RootElement.TryGetProperty("text", out var t)
                    ? t.GetString() ?? ""
                    : "";
                if (NormalizeTitle(text) == canonTitle) return block.Id;
            }
            catch { /* malformed content — skip */ }
            return null; // first heading didn't match → no dedup
        }
        return null;
    }

    private static string NormalizeTitle(string s) =>
        Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool IsContentBlock(string type) => type?.ToLowerInvariant() switch
    {
        "paragraph" or "equation" or "figure" or "table" or "code"
            or "list" or "blockquote" or "theorem" or "abstract"
            or "bibliography" => true,
        _ => false,
    };

    /// <summary>
    /// Translate LaTeX matrix environments
    /// (<c>pmatrix</c>, <c>bmatrix</c>, <c>vmatrix</c>, <c>Vmatrix</c>,
    /// <c>matrix</c>) to Typst's <c>mat(...)</c> function. LaTeX uses
    /// <c>&amp;</c> for column separators and <c>\\\\</c> for row
    /// separators; Typst uses <c>,</c> and <c>;</c>. Each variant maps
    /// to a different <c>delim:</c> argument.
    /// </summary>
    private static string ConvertMatrixEnvironments(string s)
    {
        var matrixVariants = new (string Env, string? Delim)[]
        {
            ("pmatrix", "("),
            ("bmatrix", "["),
            ("Bmatrix", "{"),
            ("vmatrix", "|"),
            ("Vmatrix", "||"),
            ("matrix",  null),
        };
        foreach (var (env, delim) in matrixVariants)
        {
            var pattern = $@"\\begin\{{{env}\}}(.*?)\\end\{{{env}\}}";
            s = Regex.Replace(s, pattern, m =>
            {
                var body = m.Groups[1].Value.Trim();
                // Strip optional row-spacing like \\\\[5pt] → \\\\
                body = Regex.Replace(body, @"\\\\\s*\[[^\]]*\]", @"\\");
                // Split rows on \\ (LaTeX line break in math)
                var rows = body.Split(new[] { @"\\" }, StringSplitOptions.None)
                    .Select(r => r.Trim())
                    .Where(r => r.Length > 0)
                    .ToList();
                // Each row: split on `&`, comma-join
                var typstRows = rows.Select(r =>
                {
                    var cols = r.Split('&').Select(c => c.Trim());
                    return string.Join(", ", cols);
                });
                var inner = string.Join("; ", typstRows);
                var delimArg = delim is null ? "" : $"delim: \"{delim}\", ";
                return $"mat({delimArg}{inner})";
            }, RegexOptions.Singleline);
        }
        return s;
    }

    /// <summary>
    /// External / placeholder image URLs can't be resolved at compile
    /// time inside Typst's sandboxed file system. Detect them and emit
    /// a drawn placeholder box so the doc still compiles via the
    /// transparent preview path; the LaTeX fallback resolves the real
    /// asset on the export path.
    /// </summary>
    private static bool IsUnresolvableFigureSrc(string src)
    {
        if (string.IsNullOrEmpty(src)) return true;
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return true;
        if (src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        if (src.StartsWith("/api/placeholder/", StringComparison.OrdinalIgnoreCase)) return true;
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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

    /// <summary>
    /// Map the editor's 24-color text palette (mirrored from
    /// FormatRailPanel + render-markers.ts) to Typst color values.
    /// Typst's stdlib has the named CSS colors; for the few that
    /// differ we emit the hex form. Unknown names pass through as
    /// a Typst identifier (most CSS color names work natively).
    /// </summary>
    private static string MapColorToTypst(string name)
    {
        return name switch
        {
            "red" => "rgb(\"#ef4444\")",
            "orange" => "rgb(\"#f97316\")",
            "amber" => "rgb(\"#f59e0b\")",
            "yellow" => "rgb(\"#eab308\")",
            "lime" => "rgb(\"#84cc16\")",
            "green" => "rgb(\"#22c55e\")",
            "emerald" => "rgb(\"#10b981\")",
            "teal" => "rgb(\"#14b8a6\")",
            "cyan" => "rgb(\"#06b6d4\")",
            "sky" => "rgb(\"#0ea5e9\")",
            "blue" => "rgb(\"#3b82f6\")",
            "indigo" => "rgb(\"#6366f1\")",
            "violet" => "rgb(\"#8b5cf6\")",
            "purple" => "rgb(\"#a855f7\")",
            "fuchsia" => "rgb(\"#d946ef\")",
            "pink" => "rgb(\"#ec4899\")",
            "rose" => "rgb(\"#f43f5e\")",
            "black" => "black",
            "gray" or "grey" => "gray",
            "darkgray" => "rgb(\"#374151\")",
            "lightgray" => "rgb(\"#9ca3af\")",
            "brown" => "rgb(\"#92400e\")",
            "olive" => "rgb(\"#65a30d\")",
            "navy" => "rgb(\"#1e3a8a\")",
            "white" => "white",
            // Unknown — wrap as a string and let Typst's color parser try.
            _ => $"rgb(\"#888888\")",
        };
    }

    /// <summary>
    /// Highlight palette — pastel tints of the text-color names. The
    /// 11-color set matches the Highlight ribbon picker.
    /// </summary>
    private static string MapHighlightColorToTypst(string name)
    {
        return name switch
        {
            "yellow" => "rgb(\"#fef08a\")",
            "orange" => "rgb(\"#fed7aa\")",
            "lime" => "rgb(\"#d9f99d\")",
            "green" => "rgb(\"#bbf7d0\")",
            "cyan" => "rgb(\"#a5f3fc\")",
            "sky" => "rgb(\"#bae6fd\")",
            "blue" => "rgb(\"#bfdbfe\")",
            "violet" => "rgb(\"#ddd6fe\")",
            "pink" => "rgb(\"#fbcfe8\")",
            "rose" => "rgb(\"#fecdd3\")",
            "gray" or "grey" => "rgb(\"#e5e7eb\")",
            _ => "rgb(\"#fef08a\")",
        };
    }

    /// <summary>
    /// Map LaTeX size keywords to em scales matching ProcessLatexText
    /// + render-markers.ts SIZE_EM table — keeps preview, LaTeX, and
    /// PDF visually aligned.
    /// </summary>
    private static string MapLatexSizeToEm(string size)
    {
        return size switch
        {
            "tiny" => "0.5",
            "scriptsize" => "0.7",
            "footnotesize" => "0.8",
            "small" => "0.9",
            "normalsize" => "1.0",
            "large" => "1.2",
            "Large" => "1.44",
            "LARGE" => "1.728",
            "huge" => "2.074",
            "Huge" => "2.488",
            _ => "1.0",
        };
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

    /// <summary>
    /// Top-N LaTeX inline commands that show up in imported document
    /// content (CVs in particular). Each command rewrites into the
    /// markdown / Typst surface that later FormatInline steps already
    /// know how to emit, so we don't grow the placeholder table or
    /// double-emit. Driven by real silent_fallback telemetry —
    /// extend as new top-rank gaps surface.
    /// </summary>
    private static string TranslateLatexLiterals(string s, Func<string, string> placeholder)
    {
        // Layout primitives.
        s = Regex.Replace(s, @"\\noindent\b\s*", "");
        s = Regex.Replace(s, @"\\indent\b\s*", "");
        s = Regex.Replace(s, @"\\par\b\s*", "\n\n");
        // Vertical skips — LaTeX `\smallskip` / `\medskip` / `\bigskip`
        // emit a small / medium / large amount of vertical space.
        // Map to Typst's `#v(...)` so the spacing actually shows up
        // (was previously stripped to empty — the user-facing Spacing
        // ribbon buttons rely on these being honoured, 2026-05-14).
        // `(?:\{\})?` matches the optional `{}` terminator the
        // editor's atom-node serialiser appends (see document-
        // serializer.ts comment on the no-arg form).
        s = Regex.Replace(s, @"\\smallskip\b(?:\{\})?\s*", _ => placeholder("#v(0.4em) "));
        s = Regex.Replace(s, @"\\medskip\b(?:\{\})?\s*", _ => placeholder("#v(0.7em) "));
        s = Regex.Replace(s, @"\\bigskip\b(?:\{\})?\s*", _ => placeholder("#v(1.2em) "));
        // Sized vertical space — `\vspace{1em}` / `\vspace*{2ex}` →
        // `#v(1em)` / `#v(2ex)`. Typst accepts the same unit suffixes.
        s = Regex.Replace(s, @"\\vspace\*?\{([^}]+)\}", m => placeholder($"#v({m.Groups[1].Value}) "));
        // Vertical fill (push content to page bottom).
        s = Regex.Replace(s, @"\\vfill\b(?:\{\})?\s*", _ => placeholder("#v(1fr) "));

        // Horizontal fill — Typst uses #h(1fr) for "spring" spacing.
        // Wrap in a placeholder so escape pass doesn't break the #h() syntax.
        s = Regex.Replace(s, @"\\hfill\b(?:\{\})?\s*", _ => placeholder("#h(1fr) "));
        // Sized horizontal space — `\hspace{1em}` / `\hspace*{2cm}` →
        // `#h(1em)` / `#h(2cm)`. Typst accepts the same unit suffixes
        // as LaTeX (em, cm, pt, ex, mm, in). The `*`-variant doesn't
        // suppress at line breaks in Typst — close enough for preview.
        s = Regex.Replace(s, @"\\hspace\*?\{([^}]+)\}", m => placeholder($"#h({m.Groups[1].Value}) "));

        // Explicit line break — LaTeX `\\` (or `\\\\` after JSON escape).
        // Both collapse to Typst's single-backslash line break in source.
        s = Regex.Replace(s, @"\\\\(?!\\)", _ => placeholder(" \\ "));
        s = Regex.Replace(s, @"\\linebreak\b\s*", _ => placeholder(" \\ "));
        s = Regex.Replace(s, @"\\newline\b\s*", _ => placeholder(" \\ "));

        // Inline font-shape commands → markdown so the existing bold/italic
        // regexes catch them on the next pass. Note \textbf{...} can nest;
        // we only translate the outermost pair, which is enough for the
        // overwhelmingly common case of a flat run of text.
        s = Regex.Replace(s, @"\\textbf\{([^{}]+)\}", "**$1**");
        s = Regex.Replace(s, @"\\textit\{([^{}]+)\}", "*$1*");
        s = Regex.Replace(s, @"\\emph\{([^{}]+)\}", "*$1*");
        s = Regex.Replace(s, @"\\texttt\{([^{}]+)\}", "`$1`");

        // Underline + small caps — no markdown equivalent; emit Typst
        // function calls via placeholder so escaping doesn't touch them.
        s = Regex.Replace(s, @"\\underline\{([^{}]+)\}",
            m => placeholder($"#underline[{EscapeTypstInline(m.Groups[1].Value)}]"));
        s = Regex.Replace(s, @"\\textsc\{([^{}]+)\}",
            m => placeholder($"#smallcaps[{EscapeTypstInline(m.Groups[1].Value)}]"));

        // Quotation environments — flatten to literal quotes since
        // Typst's #quote() at inline scope adds visual ceremony users
        // didn't ask for in body text.
        s = Regex.Replace(s, @"\\enquote\{([^{}]+)\}", "“$1”");
        s = Regex.Replace(s, @"\\textquote\{([^{}]+)\}", "“$1”");

        // Section commands accidentally placed inside paragraph content
        // (CV imports do this). Promote to Typst heading marker in-line;
        // the marker only renders as a heading when at start of line, so
        // mid-paragraph occurrences degrade gracefully to literal text.
        s = Regex.Replace(s, @"\\section\*?\{([^{}]+)\}", "\n= $1\n");
        s = Regex.Replace(s, @"\\subsection\*?\{([^{}]+)\}", "\n== $1\n");
        s = Regex.Replace(s, @"\\subsubsection\*?\{([^{}]+)\}", "\n=== $1\n");
        s = Regex.Replace(s, @"\\paragraph\*?\{([^{}]+)\}", "\n==== $1\n");

        return s;
    }

    private static string MapFont(string? family) => family?.ToLowerInvariant() switch
    {
        "sans" or "sans-serif" => "Linux Libertine",
        "mono" or "monospace" => "DejaVu Sans Mono",
        _ => "Linux Libertine",
    };

    /// <summary>
    /// Comma-separated quoted font list for Typst's <c>font: (..., ...)</c>
    /// syntax. Always ends with "New Computer Modern" — bundled with the
    /// typst binary so compile never fails on missing system fonts.
    /// </summary>
    private static string MapFontList(string? family)
    {
        var primary = MapFont(family);
        // "New Computer Modern" is the typst-bundled fallback that always
        // resolves. For monospace contexts the secondary fallback differs.
        var fallbacks = family?.ToLowerInvariant() is "mono" or "monospace"
            ? new[] { "DejaVu Sans Mono", "New Computer Modern Mono", "New Computer Modern" }
            : new[] { primary, "New Computer Modern" };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (var f in fallbacks)
        {
            if (seen.Add(f)) unique.Add(f);
        }
        return string.Join(", ", unique.Select(QuoteTypst));
    }
}

public interface ITypstExportService
{
    string BuildTypstDocument(
        Document doc,
        List<Block> blocks,
        IReadOnlyList<BlockGroup>? layoutGroups = null,
        TypstExportOptions? options = null);
    string RenderBlockForTest(Block block);
}

public class TypstExportOptions
{
    public string DocumentClass { get; set; } = "article";
    public string FontSize { get; set; } = "11pt";
    public string PaperSize { get; set; } = "a4";
}
