using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Themes;

namespace Lilia.Engines;

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
        // Journal classes that work with Lilia's standard \maketitle/\section
        // structure (no special frontmatter macros required). elsarticle /
        // llncs / acmart need per-class frontmatter templates — add them with
        // that work, not here.
        "IEEEtran",
    };

    /// <summary>
    /// Classes the pagination policy is NOT emitted for. beamer frames are not
    /// pages in the LaTeX sense — they never run short, floats do not migrate
    /// between them, and \raggedbottom / \flushbottom are meaningless there.
    /// Presentations are also out of scope (articles, reports, books), so this
    /// is a guard against breaking documents we no longer target rather than a
    /// feature gap.
    /// </summary>
    private static readonly HashSet<string> NoPaginationPolicyClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "beamer", "beamerposter",
    };

    /// <summary>
    /// Classes that load two-sided by default, and therefore start out under
    /// \flushbottom. Measured per class rather than assumed — see
    /// <see cref="Document.PaginationPolicy"/> for the probe and its results.
    /// </summary>
    private static readonly HashSet<string> TwoSideByDefaultClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "book", "amsbook", "scrbook", "memoir",
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
    /// The class actually emitted for this document: the stored one when we ship
    /// its .cls, otherwise the fallback. Shared by the class directive and the
    /// pagination policy, which have to agree about which class is in play.
    /// </summary>
    public static string ResolveClassName(Document doc, string fallbackClass = "article")
    {
        var stored = doc.LatexDocumentClass?.Trim();
        return !string.IsNullOrWhiteSpace(stored) && SafeDocumentClasses.Contains(stored)
            ? stored!
            : fallbackClass;
    }

    /// <summary>
    /// Whether this document's class starts out under <c>\flushbottom</c>. Used
    /// only to pick a safe widow/club penalty when the author has expressed no
    /// preference — never to force a bottom-fill policy of our own.
    ///
    /// Two-side and two-column each switch the standard classes to
    /// <c>\flushbottom</c> independently, which is why <c>twocolumn</c> is
    /// checked before <c>oneside</c>: a one-sided two-column article is still
    /// flush-bottomed.
    /// </summary>
    private static bool StartsFlushBottom(Document doc, string className)
    {
        var options = (doc.LatexDocumentClassOptions ?? string.Empty)
            .Split(',')
            .Select(CleanClassOption)
            .Where(t => t is not null)
            .ToArray();

        bool HasOption(string name) =>
            options.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));

        // BuildClassDirective adds `twocolumn` for Columns >= 2 unless multicol
        // owns the flow, so mirror that here rather than reading the option only.
        if (HasOption("twocolumn") || (doc.Columns >= 2 && !doc.BalancedColumns)) return true;
        if (HasOption("twoside")) return true;
        if (HasOption("oneside")) return false;
        return TwoSideByDefaultClasses.Contains(className);
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
    /// <summary>
    /// The \documentclass option for a stored paper size. Only letter used to be
    /// mapped, so a5, legal, executive and b5 all came out as a4paper.
    /// </summary>
    internal static string ClassPaperOption(string? paperSize) =>
        paperSize?.Trim().ToLowerInvariant() switch
        {
            "letter" => "letterpaper",
            "legal" => "legalpaper",
            "a5" => "a5paper",
            "b5" => "b5paper",
            "executive" => "executivepaper",
            _ => "a4paper",
        };

    public static string BuildClassDirective(
        Document doc,
        string? fontSizeOverride = null,
        string? paperSizeOverride = null,
        string fallbackClass = "article")
    {
        var stored = doc.LatexDocumentClass?.Trim();
        var usingStored = !string.IsNullOrWhiteSpace(stored) && SafeDocumentClasses.Contains(stored);
        var className = ResolveClassName(doc, fallbackClass);

        var classOpts = new List<string>();
        var fontSize = fontSizeOverride ?? $"{doc.FontSize}pt";
        if (!string.IsNullOrEmpty(fontSize)) classOpts.Add(fontSize);

        var paperSize = paperSizeOverride
            ?? ClassPaperOption(doc.PaperSize);
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

        // Pagination policy. The standard method's Tier 1: global, set once, and
        // Lilia owns the preamble — so it costs the author nothing. Tier 2
        // (\needspace, [H], \clearpage) is targeted and belongs at the end of
        // writing, which needs page-map feedback to aim.
        var policyClass = ResolveClassName(doc);
        if (!NoPaginationPolicyClasses.Contains(policyClass))
        {
            sb.AppendLine("% Pagination policy");

            // placeins[section] redefines \section to insert a \FloatBarrier, so
            // a figure cannot drift past the section it belongs to. Floats are
            // 290 questions in the corpus against page-breaking's 141 — twice
            // the pain, one package option.
            //
            // PassOptionsToPackage + a plain \usepackage is the same idiom used
            // for hyperref in LaTeXPreamble.Packages: it avoids an option clash
            // when an imported preamble already pulled placeins in. Verified to
            // compile clean on article, report, book, amsart, memoir, scrartcl,
            // scrbook, scrreprt and IEEEtran (pdflatex, 2026-07-30) — memoir and
            // the KOMA classes carry their own float machinery, so that was
            // worth checking rather than assuming.
            sb.AppendLine("\\PassOptionsToPackage{section}{placeins}");
            sb.AppendLine("\\usepackage{placeins}");

            // Bottom fill. Emitted ONLY when the author has chosen one: a
            // document with no preference keeps its class default, so adding
            // this feature cannot re-typeset anything that already exists.
            var policy = doc.PaginationPolicy?.Trim().ToLowerInvariant();
            if (policy == "ragged") sb.AppendLine("\\raggedbottom");
            else if (policy == "flush") sb.AppendLine("\\flushbottom");

            // Widows and orphans. Only two values are meaningful: finite
            // (discourage) or 10000 (forbid outright). Forbidding is safe only
            // when the page bottom is allowed to run short — under \flushbottom
            // LaTeX has to stretch the page instead, which puts back exactly the
            // gaps \raggedbottom removes. So the penalty follows the bottom
            // policy rather than being a second setting the author can get
            // wrong. displaywidowpenalty is included because Lilia documents are
            // equation-heavy and it governs widows after display math.
            var raggedInEffect = policy switch
            {
                "ragged" => true,
                "flush" => false,
                _ => !StartsFlushBottom(doc, policyClass),
            };
            var penalty = raggedInEffect ? 10000 : 300;
            sb.AppendLine($"\\widowpenalty={penalty}");
            sb.AppendLine($"\\clubpenalty={penalty}");
            sb.AppendLine($"\\displaywidowpenalty={penalty}");
        }

        // Line spacing (setspace already in default preamble — emitted
        // here only when an override is explicitly active, otherwise the
        // class default 1.0 wins).
        var lineSpacing = lineSpacingOverride ?? doc.LineSpacing;
        if (lineSpacing.HasValue && Math.Abs(lineSpacing.Value - 1.0) > 0.001)
        {
            sb.AppendLine("% Line spacing");
            // Exactly 1.5 and 2 (what the settings dialog sends) mean setspace's spacings; any other value,
            // including a \linespread factor the importer nudged off them, is written as \linespread.
            if (Math.Abs(lineSpacing.Value - 1.5) < 1e-9)
            {
                sb.AppendLine("\\onehalfspacing");
            }
            else if (Math.Abs(lineSpacing.Value - 2.0) < 1e-9)
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
        // least one slot is set — loading it unconditionally would
        // override the class default page style for every doc.
        //
        // Resolution order per slot: explicit L/C/R wins; if none of
        // the three header slots is set but the legacy HeaderText is,
        // it lands in \lhead (preserves pre-2026-05 behavior). Same
        // for footer with \rfoot.
        var hL = NullIfBlank(doc.HeaderLeft);
        var hC = NullIfBlank(doc.HeaderCenter);
        var hR = NullIfBlank(doc.HeaderRight);
        var fL = NullIfBlank(doc.FooterLeft);
        var fC = NullIfBlank(doc.FooterCenter);
        var fR = NullIfBlank(doc.FooterRight);
        if (hL is null && hC is null && hR is null && !string.IsNullOrWhiteSpace(doc.HeaderText))
        {
            hL = doc.HeaderText;
        }
        if (fL is null && fC is null && fR is null && !string.IsNullOrWhiteSpace(doc.FooterText))
        {
            fR = doc.FooterText;
        }
        if (hL is not null || hC is not null || hR is not null
         || fL is not null || fC is not null || fR is not null)
        {
            sb.AppendLine("% Header / footer");
            sb.AppendLine("\\usepackage{fancyhdr}");
            sb.AppendLine("\\pagestyle{fancy}");
            sb.AppendLine("\\fancyhf{}");
            if (hL is not null) sb.AppendLine($"\\lhead{{{EscapeUserText(hL)}}}");
            if (hC is not null) sb.AppendLine($"\\chead{{{EscapeUserText(hC)}}}");
            if (hR is not null) sb.AppendLine($"\\rhead{{{EscapeUserText(hR)}}}");
            if (fL is not null) sb.AppendLine($"\\lfoot{{{EscapeUserText(fL)}}}");
            if (fC is not null) sb.AppendLine($"\\cfoot{{{EscapeUserText(fC)}}}");
            if (fR is not null) sb.AppendLine($"\\rfoot{{{EscapeUserText(fR)}}}");
            // \fancyhf{} above clears the automatic page number, so a header
            // alone made the numbers vanish from the foot of every page. When
            // no footer slot is set, put the page number back where the class
            // default has it (centred), unless numbering is switched off.
            if (fL is null && fC is null && fR is null
                && !string.Equals(doc.PageNumbering?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("\\cfoot{\\thepage}");
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
        else if (string.Equals(doc.FontFamily?.Trim(), "sans-serif", StringComparison.OrdinalIgnoreCase))
        {
            // The settings dialog offers "Sans Serif (Helvetica-like)" and Ask Lilia's validator accepts it,
            // but nothing was emitted, so the PDF stayed serif (5 Oct review). helvet (psnfss) is the
            // Helvetica clone in every TeX Live; scaled to sit with Latin Modern math.
            sb.AppendLine("% Font family (sans-serif)");
            sb.AppendLine("\\usepackage[scaled=0.92]{helvet}");
            sb.AppendLine("\\renewcommand{\\familydefault}{\\sfdefault}");
        }
        else if (string.Equals(doc.FontFamily?.Trim(), "monospace", StringComparison.OrdinalIgnoreCase))
        {
            // "Monospace (Courier-like)": same gap as sans-serif.
            sb.AppendLine("% Font family (monospace)");
            sb.AppendLine("\\usepackage{courier}");
            sb.AppendLine("\\renewcommand{\\familydefault}{\\ttdefault}");
        }

        return sb.ToString();
    }

    /// <summary>How the theme line treats a theme this server cannot compile.</summary>
    public enum ThemeUse
    {
        /// <summary>A compile (preview, PDF): an unavailable theme fails with a message, never a substitute face.</summary>
        Compile,
        /// <summary>A .tex / .zip download, compiled elsewhere (Overleaf): the line is written as stored.</summary>
        Export,
        /// <summary>Per-block validation context: an unavailable theme is left out rather than failing every block.</summary>
        Validation,
    }

    /// <summary>Classes that define \chapter, where the package on its own would colour chapters.</summary>
    private static readonly IReadOnlySet<string> ChapterTopClasses = Lilia.Core.Models.HeadingCommands.ChapterClasses;

    /// <summary>
    /// The document theme's managed preamble line (Document settings → Look), or an empty string
    /// for Classic with today's tables and for a class that sets its own look. Callers emit it just
    /// BEFORE the custom preamble, so the author's own settings win. The table settings (Look →
    /// Tables) add <c>tables=…</c>, <c>tabledensity=compact</c> and <c>captions=below</c> where they
    /// differ from the theme's defaults; Classic with such a setting writes
    /// <c>\usepackage[theme=classic, tables=banded]{lilia-theme}</c>.
    ///
    /// <code>
    /// % Document theme (Document settings → Look)
    /// \usepackage[theme=index, paper=theme]{lilia-theme}
    /// \liliaPinColour{3}{7}
    /// </code>
    ///
    /// <para>Index colours "the top numbered heading the document actually prints". In a class that
    /// has \chapter a level-1 heading prints as \chapter (HeadingCommands), so the line says
    /// <c>top=chapter</c> when there is one (or an embed block prints its own \chapter), and
    /// <c>top=section</c> otherwise.</para>
    ///
    /// <para>Index pins are stored per heading block; each is written as the number that heading
    /// has now (its position among the numbered level-1 headings), so a pin follows its heading
    /// when headings move. When an embed prints its own \chapter the count would be out of step,
    /// so no pin is written.</para>
    ///
    /// <para>A beamer document takes Classic, which writes nothing (beamer's default look), or a
    /// theme's beamer version: <c>\usetheme{LiliaCerulean}</c>, <c>\usetheme{LiliaIndex}</c>,
    /// <c>\usetheme{LiliaExposition}</c>, with <c>[printsafe]</c> on white paper or for a print-safe
    /// export. Index's pins follow as on any paper, numbered by the deck's sections
    /// (<c>\liliaPinColour{2}{7}</c>). The table settings are ignored there. A theme the class
    /// cannot use (left over from a class change) prints as Classic:
    /// <see cref="DocumentLook.ForClass"/>.</para>
    /// </summary>
    /// <param name="bodyBlocks">The blocks the body will contain, in order.</param>
    /// <param name="lookOverride">An export's look (Export PDF: Look ▾ / Print-safe); null uses the stored one.</param>
    public static string BuildThemeLine(
        Document doc,
        IEnumerable<Block>? bodyBlocks,
        DocumentLook? lookOverride = null,
        ThemeUse use = ThemeUse.Compile)
    {
        // Under a class that sets its own look nothing is written: tables stay ruled.
        if (ThemeLock.Reason(doc.LatexDocumentClass) is not null) return string.Empty;
        var look = (lookOverride ?? DocumentLook.Parse(doc.Look)).ForClass(doc.LatexDocumentClass);
        if (ThemeLock.IsBeamer(doc.LatexDocumentClass)) return BuildBeamerThemeLine(look, bodyBlocks, use);
        if (!look.LoadsPackage) return string.Empty;
        if (look.IsClassic)
        {
            // Classic with a table setting: the package's table part only (Classic loads nothing else).
            var classic = new StringBuilder();
            classic.AppendLine("% Document theme (Document settings → Look): Classic, with its table settings.");
            classic.AppendLine($"\\usepackage[{string.Join(", ", new[] { "theme=classic" }.Concat(look.TableOptions()))}]{{{ThemeCatalog.PackageName}}}");
            return classic.ToString();
        }
        if (use != ThemeUse.Export && ThemeAvailability.WhyUnavailable(look.Theme) is { } why)
        {
            if (use == ThemeUse.Validation) return string.Empty;
            throw new ThemeUnavailableException(why);
        }

        var options = new List<string> { $"theme={look.Theme}", $"paper={look.Paper}" };
        if (look.PrintSafe) options.Add("printsafe");
        if (OwnsPageFoot(doc)) options.Add("foottab=false");
        var blocks = (bodyBlocks ?? Enumerable.Empty<Block>()).ToList();
        // Pins are numbered by the level-1 headings; an embed that prints its own \chapter would put
        // the count out of step, so then no pin is written.
        var rawChapters = blocks.Any(PrintsChapter);
        if (ChapterTopClasses.Contains(ResolveClassName(doc)))
        {
            var chapterTop = rawChapters || blocks.Any(IsNumberedTopHeading);
            options.Add(chapterTop ? "top=chapter" : "top=section");
        }
        // Look → Tables: only what differs from the theme's defaults.
        options.AddRange(look.TableOptions());

        var sb = new StringBuilder();
        sb.AppendLine("% Document theme (Document settings → Look). Before the custom preamble, so the author's settings win.");
        sb.AppendLine($"\\usepackage[{string.Join(", ", options)}]{{{ThemeCatalog.PackageName}}}");

        if (!rawChapters) AppendPins(sb, look, blocks);
        return sb.ToString();
    }

    /// <summary>
    /// Index's pins, one <c>\liliaPinColour{n}{k}</c> per pinned heading, n being its number among
    /// the numbered level-1 headings (\chapter or \section in a document, \section in a deck).
    /// </summary>
    private static void AppendPins(StringBuilder sb, DocumentLook look, IReadOnlyList<Block> blocks)
    {
        if (look.Pins.Count == 0 || look.Theme != ThemeCatalog.Index) return;
        var number = 0;
        foreach (var block in blocks)
        {
            // Appendices take the plain sequence and their counter restarts, so a pin past
            // this point would name a main chapter's number.
            if (Themes.ThemeSections.StartsAppendix(block)) break;
            if (!IsNumberedTopHeading(block)) continue;
            number++;
            if (look.Pins.TryGetValue(block.Id.ToString(), out var k))
                sb.AppendLine($"\\liliaPinColour{{{number}}}{{{k}}}");
        }
    }

    /// <summary>
    /// A beamer deck's managed line: nothing for Classic (beamer's own look), one
    /// <c>\usetheme</c> for a theme's beamer version (Cerulean, Index, Exposition), then Index's
    /// pins. Table settings do not apply to beamer.
    /// </summary>
    private static string BuildBeamerThemeLine(DocumentLook look, IEnumerable<Block>? bodyBlocks, ThemeUse use)
    {
        if (ThemeCatalog.Find(look.Theme) is not { Beamer: { } beamer }) return string.Empty;
        if (use != ThemeUse.Export && ThemeAvailability.WhyUnavailable(look.Theme, ThemeLock.Beamer) is { } why)
        {
            if (use == ThemeUse.Validation) return string.Empty;
            throw new ThemeUnavailableException(why);
        }
        var printSafe = look.PrintSafe || look.Paper == DocumentLook.PaperWhite;
        var sb = new StringBuilder();
        sb.AppendLine("% Document theme (Document settings → Look). Before the custom preamble, so the author's settings win.");
        sb.AppendLine(printSafe ? $"\\usetheme[printsafe]{{{beamer.Theme}}}" : $"\\usetheme{{{beamer.Theme}}}");
        AppendPins(sb, look, (bodyBlocks ?? Enumerable.Empty<Block>()).ToList());
        return sb.ToString();
    }

    /// <summary>
    /// The author set the running header/footer or turned page numbers off: the theme then
    /// leaves the page style alone instead of drawing its foot tab over their choice.
    /// </summary>
    private static bool OwnsPageFoot(Document doc) =>
        string.Equals(doc.PageNumbering?.Trim(), "none", StringComparison.OrdinalIgnoreCase)
        || new[] { doc.HeaderText, doc.FooterText, doc.HeaderLeft, doc.HeaderCenter, doc.HeaderRight,
                   doc.FooterLeft, doc.FooterCenter, doc.FooterRight }.Any(v => !string.IsNullOrWhiteSpace(v));

    private static readonly System.Text.RegularExpressions.Regex ChapterCommand =
        new(@"\\chapter(?![A-Za-z@])", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>An embed block (raw LaTeX, emitted as written) whose code prints a \chapter.</summary>
    internal static bool PrintsChapter(Block block)
    {
        if (!string.Equals(block.Type, "embed", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var c = block.Content.RootElement;
            var code = c.TryGetProperty("code", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (string.IsNullOrEmpty(code)) return false;
            var uncommented = System.Text.RegularExpressions.Regex.Replace(code, @"(?<!\\)%[^\r\n]*", "");
            return ChapterCommand.IsMatch(uncommented);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A heading block that renders as a numbered \section (level 1, not numbered:false).</summary>
    internal static bool IsNumberedTopHeading(Block block)
    {
        if (!string.Equals(block.Type, "heading", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(block.Type, "header", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var c = block.Content.RootElement;
            var level = c.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var lv) ? lv : 1;
            var numbered = !c.TryGetProperty("numbered", out var n) || n.ValueKind != JsonValueKind.False;
            return level <= 1 && numbered;
        }
        catch
        {
            return false;
        }
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
    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

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
