using System.Globalization;
using System.Text;
using System.Text.Json;
using Lilia.Core.Entities;

namespace Lilia.Api.Services;

/// <summary>
/// Single source of truth for building the LaTeX preamble from a Document.
/// Used by both <see cref="LaTeXExportService"/> (zip / single-file export)
/// and <see cref="RenderService"/> (live preview render). Emits the
/// `\documentclass[…]{…}` directive plus every layout setting stored on
/// Document.* (margins, line spacing, page numbering, header/footer,
/// paragraph indent, columns, font family).
///
/// History: this consolidates two near-identical paths previously living
/// in LaTeXExportService.BuildDocumentClassDirective and
/// RenderService.BuildDocumentClassDirectiveFromDoc. Phase A of the
/// documentclass-first epic (LILIA-119/120).
/// </summary>
public static class LaTeXPreambleBuilder
{
    /// <summary>
    /// Classes the DO App Platform container reliably provides. Anything
    /// outside this list (mnras, aastex, pnas, IEEEtran, etc.) requires a
    /// .cls we don't ship, so we fall back to article and rely on shim
    /// commands. Mirrors the lists in LaTeXExportService and RenderService;
    /// kept here as the canonical copy.
    /// </summary>
    private static readonly HashSet<string> SafeDocumentClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "article", "report", "book", "letter", "minimal",
        "amsart", "amsbook", "amsproc",
        "memoir",
        "scrartcl", "scrbook", "scrreprt",
        "beamer", "beamerposter",
    };

    /// <summary>
    /// When falling back to article, only forward options that article
    /// actually understands; drop class-specific garbage silently.
    /// </summary>
    private static readonly HashSet<string> ArticleKnownOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "10pt", "11pt", "12pt",
        "a4paper", "a5paper", "letterpaper", "legalpaper", "executivepaper", "b5paper",
        "landscape", "portrait",
        "onecolumn", "twocolumn",
        "oneside", "twoside",
        "openright", "openany",
        "final", "draft",
        "titlepage", "notitlepage",
        "fleqn", "leqno",
        "openbib",
    };

    /// <summary>
    /// Map the supported font-family setting onto a native LaTeX package.
    /// Decision (LILIA-120): drop Georgia (no native pdflatex equivalent
    /// without xelatex+fontspec); add Palatino and Bookman. The settings
    /// dialog cleanup is a separate ticket — if the UI still emits
    /// Georgia, we fall through silently and let the class default win.
    /// </summary>
    private static readonly Dictionary<string, string> FontFamilyPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["charter"] = "charter",
        ["times"] = "mathptmx",
        ["palatino"] = "palatino",
        ["bookman"] = "bookman",
        // TODO(LILIA-120): Georgia is intentionally absent — UI cleanup
        // pending. Picking Georgia today produces no font emission.
    };

    /// <summary>
    /// Cleans one class-option token: strip any LaTeX comment, trim,
    /// drop multi-line garbage. Same logic as the duplicates in
    /// LaTeXExportService and RenderService — owned here now.
    /// </summary>
    private static string? CleanClassOption(string raw)
    {
        var t = raw;
        var pct = t.IndexOf('%');
        if (pct >= 0) t = t.Substring(0, pct);
        t = t.Trim();
        if (t.Length == 0) return null;
        if (t.Any(c => c == '\r' || c == '\n' || c == '\t')) return null;
        return t;
    }

    /// <summary>
    /// Result of preamble assembly. Callers stitch it into their full
    /// output: <see cref="ClassDirective"/> at the top, then
    /// <see cref="LayoutPreamble"/> after their package list, and
    /// <see cref="BodyOpener"/>/<see cref="BodyCloser"/> bracket the
    /// document body when balanced multicols are requested.
    /// </summary>
    public sealed record PreambleResult(
        string ClassDirective,
        string LayoutPreamble,
        string BodyOpener,
        string BodyCloser);

    /// <summary>
    /// Build the class directive in isolation. Used by callers that
    /// need to splice their own package list between the directive and
    /// the layout block (the typical export shape).
    /// </summary>
    /// <param name="doc">Document with all layout settings.</param>
    /// <param name="fontSizeOverride">
    /// When non-null, used as the Xpt class option. Defaults to
    /// <c>{doc.FontSize}pt</c>. Export callers pass the option-derived value;
    /// render callers leave it null.
    /// </param>
    /// <param name="paperSizeOverride">
    /// When non-null, used as the paper-size class option (e.g.
    /// "a4paper"). Defaults to a4paper / letterpaper based on doc.PaperSize.
    /// </param>
    /// <param name="fallbackClass">
    /// Class name to use when the stored class isn't in
    /// <see cref="SafeDocumentClasses"/>. Defaults to "article".
    /// </param>
    public static string BuildClassDirective(
        Document doc,
        string? fontSizeOverride = null,
        string? paperSizeOverride = null,
        string fallbackClass = "article")
    {
        var stored = doc.LatexDocumentClass?.Trim();
        var usingStored = !string.IsNullOrWhiteSpace(stored) && SafeDocumentClasses.Contains(stored);
        var className = usingStored ? stored! : fallbackClass;

        var classOpts = new List<string>();
        var fontSize = fontSizeOverride ?? $"{doc.FontSize}pt";
        if (!string.IsNullOrEmpty(fontSize)) classOpts.Add(fontSize);

        var paperSize = paperSizeOverride
            ?? (string.Equals(doc.PaperSize, "letter", StringComparison.OrdinalIgnoreCase)
                ? "letterpaper"
                : "a4paper");
        if (!string.IsNullOrEmpty(paperSize)) classOpts.Add(paperSize);

        if (!string.IsNullOrWhiteSpace(doc.LatexDocumentClassOptions))
        {
            foreach (var rawTok in doc.LatexDocumentClassOptions.Split(','))
            {
                var t = CleanClassOption(rawTok);
                if (t == null) continue;
                if (!usingStored && !ArticleKnownOptions.Contains(t)) continue;
                if (!classOpts.Contains(t)) classOpts.Add(t);
            }
        }

        // landscape class option — flips paper orientation. Independent
        // of paper size: A4 landscape is still A4. The portrait default
        // is implicit so we only emit when explicitly set.
        if (string.Equals(doc.Orientation, "landscape", StringComparison.OrdinalIgnoreCase)
            && !classOpts.Any(o => string.Equals(o, "landscape", StringComparison.OrdinalIgnoreCase)))
        {
            classOpts.Add("landscape");
        }

        // twocolumn class option only when columns >= 2 AND balanced
        // columns is OFF — balanced columns uses the multicol package
        // (added below in BuildLayoutPreamble) which is incompatible
        // with the twocolumn class option.
        if (doc.Columns >= 2
            && !doc.BalancedColumns
            && !classOpts.Any(o => string.Equals(o, "twocolumn", StringComparison.OrdinalIgnoreCase)))
        {
            classOpts.Add("twocolumn");
        }

        // If balanced columns is on but stored options forced "twocolumn"
        // back in, strip it — multicol owns column flow in that mode.
        if (doc.BalancedColumns)
        {
            classOpts.RemoveAll(o => string.Equals(o, "twocolumn", StringComparison.OrdinalIgnoreCase));
        }

        return classOpts.Count > 0
            ? $"\\documentclass[{string.Join(",", classOpts)}]{{{className}}}"
            : $"\\documentclass{{{className}}}";
    }

    /// <summary>
    /// Build the layout-settings block — emitted AFTER the class directive
    /// and the default package preamble, BEFORE \begin{document}. Honours
    /// margins, line spacing, paragraph indent, page numbering,
    /// header/footer, font family, column gap/separator, and the
    /// multicol package when BalancedColumns is on.
    /// </summary>
    /// <param name="doc">Document with all layout settings.</param>
    /// <param name="lineSpacingOverride">
    /// When set, takes precedence over <c>doc.LineSpacing</c>. Export
    /// callers pass the option-derived value; render callers leave null
    /// (in which case doc.LineSpacing wins, defaulting to 1.0 if absent).
    /// </param>
    public static string BuildLayoutPreamble(Document doc, double? lineSpacingOverride = null)
    {
        var sb = new StringBuilder();

        // Margins via geometry
        var marginParts = new List<string>();
        if (!string.IsNullOrEmpty(doc.MarginTop)) marginParts.Add($"top={doc.MarginTop}");
        if (!string.IsNullOrEmpty(doc.MarginBottom)) marginParts.Add($"bottom={doc.MarginBottom}");
        if (!string.IsNullOrEmpty(doc.MarginLeft)) marginParts.Add($"left={doc.MarginLeft}");
        if (!string.IsNullOrEmpty(doc.MarginRight)) marginParts.Add($"right={doc.MarginRight}");
        if (marginParts.Count > 0)
        {
            sb.AppendLine("% Page margins");
            sb.AppendLine($"\\usepackage[{string.Join(",", marginParts)}]{{geometry}}");
        }

        // Line spacing (setspace already in default preamble — emitted
        // here only when an override is explicitly active, otherwise the
        // class default 1.0 wins).
        var lineSpacing = lineSpacingOverride ?? doc.LineSpacing;
        if (lineSpacing.HasValue && Math.Abs(lineSpacing.Value - 1.0) > 0.001)
        {
            sb.AppendLine("% Line spacing");
            if (Math.Abs(lineSpacing.Value - 1.5) < 0.01)
            {
                sb.AppendLine("\\onehalfspacing");
            }
            else if (Math.Abs(lineSpacing.Value - 2.0) < 0.01)
            {
                sb.AppendLine("\\doublespacing");
            }
            else
            {
                // \linespread is the LaTeX-classic command and what users
                // pasting from arXiv expect; setspace's \setstretch is
                // equivalent but less idiomatic in the source pane.
                sb.AppendLine($"\\linespread{{{lineSpacing.Value.ToString("0.##", CultureInfo.InvariantCulture)}}}");
            }
        }

        // Paragraph indent
        if (!string.IsNullOrWhiteSpace(doc.ParagraphIndent))
        {
            sb.AppendLine("% Paragraph indent");
            if (string.Equals(doc.ParagraphIndent, "none", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("\\setlength{\\parindent}{0pt}");
            }
            else
            {
                sb.AppendLine($"\\setlength{{\\parindent}}{{{doc.ParagraphIndent}}}");
            }
        }

        // Column gap + separator. Only meaningful when there's actually
        // more than one column, but emitting them harmlessly when
        // Columns == 1 is fine (LaTeX just stores the lengths).
        if (doc.Columns >= 2 || doc.BalancedColumns)
        {
            // ColumnGap is a double in cm; use invariant culture so we
            // don't emit "1,5cm" on locales with comma decimal sep.
            sb.AppendLine($"\\setlength{{\\columnsep}}{{{doc.ColumnGap.ToString("0.##", CultureInfo.InvariantCulture)}cm}}");
            // Separator: "line" → 0.4pt rule between columns; anything
            // else (including null / "none") → 0pt (no rule).
            var rule = string.Equals(doc.ColumnSeparator, "line", StringComparison.OrdinalIgnoreCase)
                || string.Equals(doc.ColumnSeparator, "rule", StringComparison.OrdinalIgnoreCase)
                ? "0.4pt"
                : "0pt";
            sb.AppendLine($"\\setlength{{\\columnseprule}}{{{rule}}}");
        }

        // Balanced columns require multicol; the body wrapper is added
        // by BuildBodyOpener/Closer, but the package itself loads here.
        if (doc.BalancedColumns)
        {
            sb.AppendLine("\\usepackage{multicol}");
        }

        // Page numbering — drives \pagenumbering / \pagestyle{empty}.
        // Default behaviour (null) leaves the class default in place.
        if (!string.IsNullOrWhiteSpace(doc.PageNumbering))
        {
            var pn = doc.PageNumbering.Trim().ToLowerInvariant();
            if (pn == "none")
            {
                sb.AppendLine("\\pagestyle{empty}");
            }
            else if (pn == "roman" || pn == "arabic" || pn == "alph" || pn == "Roman" || pn == "Alph")
            {
                sb.AppendLine($"\\pagenumbering{{{pn}}}");
            }
        }

        // Header / footer via fancyhdr. We only load fancyhdr when at
        // least one is set — loading it unconditionally would override
        // the class default page style for every doc.
        var hasHeader = !string.IsNullOrWhiteSpace(doc.HeaderText);
        var hasFooter = !string.IsNullOrWhiteSpace(doc.FooterText);
        if (hasHeader || hasFooter)
        {
            sb.AppendLine("% Header / footer");
            sb.AppendLine("\\usepackage{fancyhdr}");
            sb.AppendLine("\\pagestyle{fancy}");
            sb.AppendLine("\\fancyhf{}");
            if (hasHeader)
            {
                sb.AppendLine($"\\lhead{{{EscapeUserText(doc.HeaderText!)}}}");
            }
            if (hasFooter)
            {
                sb.AppendLine($"\\rfoot{{{EscapeUserText(doc.FooterText!)}}}");
            }
        }

        // Font family. Native pdflatex packages only — Georgia is
        // intentionally not in the map; see FontFamilyPackages comment.
        if (!string.IsNullOrWhiteSpace(doc.FontFamily)
            && FontFamilyPackages.TryGetValue(doc.FontFamily.Trim(), out var pkg))
        {
            sb.AppendLine($"% Font family ({doc.FontFamily})");
            sb.AppendLine($"\\usepackage{{{pkg}}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Body opener — emitted right after \begin{document} (and any
    /// title/abstract) when balanced multicols are active. Empty
    /// otherwise.
    /// </summary>
    public static string BuildBodyOpener(Document doc)
    {
        if (doc.BalancedColumns)
        {
            var n = Math.Max(2, doc.Columns);
            return $"\\begin{{multicols}}{{{n}}}";
        }
        return string.Empty;
    }

    /// <summary>
    /// Body closer — pairs with <see cref="BuildBodyOpener"/>. Emitted
    /// just before \end{document} when balanced multicols are active.
    /// </summary>
    public static string BuildBodyCloser(Document doc) =>
        doc.BalancedColumns ? "\\end{multicols}" : string.Empty;

    /// <summary>
    /// Convenience: build everything in one call for callers that just
    /// want the four pieces handed back together.
    /// </summary>
    public static PreambleResult Build(
        Document doc,
        string? fontSizeOverride = null,
        string? paperSizeOverride = null,
        string fallbackClass = "article",
        double? lineSpacingOverride = null)
    {
        return new PreambleResult(
            ClassDirective: BuildClassDirective(doc, fontSizeOverride, paperSizeOverride, fallbackClass),
            LayoutPreamble: BuildLayoutPreamble(doc, lineSpacingOverride),
            BodyOpener: BuildBodyOpener(doc),
            BodyCloser: BuildBodyCloser(doc));
    }

    /// <summary>
    /// Escape user-supplied text destined for a LaTeX argument
    /// (header / footer). We can't run the full LaTeX escape table
    /// here — the caller may legitimately want bold or math — but at
    /// minimum we neutralise the structural metacharacters that would
    /// break compilation: backslash, braces, percent, hash, tilde,
    /// caret, ampersand, dollar, underscore. Mirrors the conservative
    /// escape used by EscapeLatex in LaTeXExportService for plain text.
    /// </summary>
    private static string EscapeUserText(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\textbackslash{}"); break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '%': sb.Append("\\%"); break;
                case '#': sb.Append("\\#"); break;
                case '$': sb.Append("\\$"); break;
                case '&': sb.Append("\\&"); break;
                case '_': sb.Append("\\_"); break;
                case '~': sb.Append("\\textasciitilde{}"); break;
                case '^': sb.Append("\\textasciicircum{}"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
