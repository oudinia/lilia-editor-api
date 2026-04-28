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

        foreach (var block in blocks.OrderBy(b => b.SortOrder))
        {
            if (titleDuplicateBlockId.HasValue && block.Id == titleDuplicateBlockId.Value)
                continue;
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
                // Match both the canonical camelCase (BlockTypes constants)
                // and the all-lowercase forms that legacy / imported data
                // stores. Same casing tolerance the LaTeX path applies.
                "tableOfContents" or "tableofcontents" or "toc" => "#outline()",
                "pageBreak" or "pagebreak" or "page_break" => "#pagebreak()",
                "columnBreak" or "columnbreak" or "column_break" => "#colbreak()",
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
        var ordered = ResolveOrdered(content);
        var sb = new StringBuilder();
        AppendListItems(items, ordered, depth: 0, sb);
        return sb.ToString().TrimEnd();
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

        // 1. Inline math $x^2$ — Typst uses same $...$ syntax, BUT
        //    LaTeX math commands ($\mathbb{R}$, $\int$ etc.) need the
        //    same translation that equation blocks get. Otherwise an
        //    inline math literal inside a paragraph/theorem/abstract
        //    breaks the whole-document Typst compile and forces a
        //    silent_fallback to pdflatex.
        s = Regex.Replace(s, @"\$([^$]+)\$", m => Ph($"${LatexMathToTypst(m.Groups[1].Value)}$"));

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

        // \frac{a}{b} → frac(a, b) — Typst's function-call syntax.
        s = Regex.Replace(s, @"\\frac\{([^{}]+)\}\{([^{}]+)\}", "frac($1, $2)");

        // \sqrt{x} → sqrt(x) ; \sqrt[n]{x} → root(n, x)
        s = Regex.Replace(s, @"\\sqrt\[([^\]]+)\]\{([^{}]+)\}", "root($1, $2)");
        s = Regex.Replace(s, @"\\sqrt\{([^{}]+)\}", "sqrt($1)");

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
        // Layout primitives that produce no visible output in Typst.
        s = Regex.Replace(s, @"\\noindent\b\s*", "");
        s = Regex.Replace(s, @"\\indent\b\s*", "");
        s = Regex.Replace(s, @"\\par\b\s*", "\n\n");
        s = Regex.Replace(s, @"\\medskip\b\s*", "");
        s = Regex.Replace(s, @"\\smallskip\b\s*", "");
        s = Regex.Replace(s, @"\\bigskip\b\s*", "");

        // Horizontal fill — Typst uses #h(1fr) for "spring" spacing.
        // Wrap in a placeholder so escape pass doesn't break the #h() syntax.
        s = Regex.Replace(s, @"\\hfill\b\s*", _ => placeholder("#h(1fr) "));

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
    string BuildTypstDocument(Document doc, List<Block> blocks, TypstExportOptions? options = null);
    string RenderBlockForTest(Block block);
}

public class TypstExportOptions
{
    public string DocumentClass { get; set; } = "article";
    public string FontSize { get; set; } = "11pt";
    public string PaperSize { get; set; } = "a4";
}
