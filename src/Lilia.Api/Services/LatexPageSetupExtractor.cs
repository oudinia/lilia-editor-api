using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Import.Models;

namespace Lilia.Api.Services;

/// <summary>
/// The page setup a LaTeX source asks for, as Lilia document settings.
///
/// <para>The importer (<c>ILatexParser</c>) reads the blocks and records some of
/// the preamble in <see cref="ImportMetadata"/>: font size, paper size, columns,
/// leftover class options, the raw <c>geometry</c> string, a line-spacing token
/// and the raw fancyhdr lines. It does not turn any of that into settings, and
/// it does not read <c>\pagenumbering</c>, the header/footer commands, the
/// paragraph indent, the column gap or the font packages at all. This does, on
/// top of what the parser already found, and reports what it could not carry so
/// the caller can say so rather than lose it silently.</para>
///
/// <para>Every value goes through <see cref="DocumentSettingsValidator"/> on its
/// own: one value Lilia cannot hold (a margin written as
/// <c>0.8\textwidth</c>) is reported and skipped, it does not sink the rest.</para>
/// </summary>
public static class LatexPageSetupExtractor
{
    public sealed record Result(
        DocumentSettingsValidator.Input Settings,
        string? DocumentClass,
        string? PackagesJson,
        string? Sides,
        bool TitlePage,
        IReadOnlyList<string> Applied,
        IReadOnlyList<string> NotApplied);

    private static readonly Regex Comment = new(@"(?<!\\)%[^\n]*", RegexOptions.Compiled);

    public static Result Extract(string tex, ImportMetadata meta)
    {
        var applied = new List<string>();
        var notes = new List<string>();
        var input = new DocumentSettingsValidator.Input();

        var text = Comment.Replace(tex ?? "", "");
        var begin = text.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var pre = begin >= 0 ? text[..begin] : text;

        // One value at a time: keep it if Lilia accepts it, otherwise say why.
        void Set(string label, Func<DocumentSettingsValidator.Input, DocumentSettingsValidator.Input> set, string shown)
        {
            var trial = set(new DocumentSettingsValidator.Input());
            var check = DocumentSettingsValidator.Validate(trial);
            if (!check.Ok) { notes.Add($"{label}: not applied ({string.Join(" ", check.Errors)})"); return; }
            input = set(input);
            applied.Add($"{label} = {shown}");
        }

        // ── \documentclass options ─────────────────────────────────────────
        if (meta.FontSize is { } fs)
        {
            if (DocumentSettingsValidator.FontSizes.Contains(fs)) Set("fontSize", i => i with { FontSize = fs }, $"{fs}pt");
            else notes.Add($"fontSize: {fs}pt is not available (10, 11 or 12 pt only)");
        }
        if (!string.IsNullOrEmpty(meta.PaperSize)) Set("paperSize", i => i with { PaperSize = meta.PaperSize }, meta.PaperSize!);
        if (meta.Columns is { } cols) Set("columns", i => i with { Columns = cols }, cols.ToString(CultureInfo.InvariantCulture));

        var options = (meta.DocumentClassOptions ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var landscape = options.Any(o => o.Equals("landscape", StringComparison.OrdinalIgnoreCase));
        var twoside = options.Any(o => o.Equals("twoside", StringComparison.OrdinalIgnoreCase));
        var titlePage = options.Any(o => o.Equals("titlepage", StringComparison.OrdinalIgnoreCase));
        var leftover = options.Where(o => !o.Equals("landscape", StringComparison.OrdinalIgnoreCase)
            && !o.Equals("twoside", StringComparison.OrdinalIgnoreCase)
            && !o.Equals("oneside", StringComparison.OrdinalIgnoreCase)
            && !o.Equals("titlepage", StringComparison.OrdinalIgnoreCase)
            && !o.Equals("notitlepage", StringComparison.OrdinalIgnoreCase)
            && !o.Equals("portrait", StringComparison.OrdinalIgnoreCase)).ToList();
        if (leftover.Count > 0) notes.Add($"documentclass options not applied: {string.Join(", ", leftover)}");

        // ── geometry ───────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(meta.GeometryOptions))
        {
            foreach (var part in SplitTopLevel(meta.GeometryOptions!))
            {
                var eq = part.IndexOf('=');
                var key = (eq < 0 ? part : part[..eq]).Trim().ToLowerInvariant();
                var val = eq < 0 ? "" : part[(eq + 1)..].Trim().Trim('{', '}').Replace(" ", "");
                switch (key)
                {
                    case "margin":
                        // geometry's margin=<len> is the size of all four margins.
                        SetMargin("marginTop", val, (i, v) => i with { MarginTop = v });
                        SetMargin("marginBottom", val, (i, v) => i with { MarginBottom = v });
                        SetMargin("marginLeft", val, (i, v) => i with { MarginLeft = v });
                        SetMargin("marginRight", val, (i, v) => i with { MarginRight = v });
                        break;
                    case "hmargin": case "lr":
                        SetMargin("marginLeft", val, (i, v) => i with { MarginLeft = v });
                        SetMargin("marginRight", val, (i, v) => i with { MarginRight = v });
                        break;
                    case "vmargin": case "tb":
                        SetMargin("marginTop", val, (i, v) => i with { MarginTop = v });
                        SetMargin("marginBottom", val, (i, v) => i with { MarginBottom = v });
                        break;
                    case "left": case "lmargin": case "inner":
                        SetMargin("marginLeft", val, (i, v) => i with { MarginLeft = v }); break;
                    case "right": case "rmargin": case "outer":
                        SetMargin("marginRight", val, (i, v) => i with { MarginRight = v }); break;
                    case "top": case "tmargin":
                        SetMargin("marginTop", val, (i, v) => i with { MarginTop = v }); break;
                    case "bottom": case "bmargin":
                        SetMargin("marginBottom", val, (i, v) => i with { MarginBottom = v }); break;
                    case "landscape": landscape = true; break;
                    case "portrait": case "": break;
                    case "a4paper": case "letterpaper": case "legalpaper": case "a5paper": case "b5paper": case "executivepaper":
                        Set("paperSize", i => i with { PaperSize = key.Replace("paper", "") }, key.Replace("paper", ""));
                        break;
                    default:
                        notes.Add($"geometry option '{part.Trim()}' not applied (Lilia sets the four margins, not text or body sizes)");
                        break;
                }
            }
        }
        void SetMargin(string label, string val, Func<DocumentSettingsValidator.Input, string, DocumentSettingsValidator.Input> put) =>
            Set(label, i => put(i, val), val);

        if (landscape) Set("orientation", i => i with { Orientation = "landscape" }, "landscape");

        // ── line spacing ───────────────────────────────────────────────────
        double? spacing = null;
        switch (meta.LineSpacing?.Trim().ToLowerInvariant())
        {
            case "double": spacing = 2.0; break;
            case "onehalf": spacing = 1.5; break;
            case "single": spacing = 1.0; break;
            case null or "": break;
            case var s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v): spacing = v; break;
            case var s: notes.Add($"line spacing '{s}' not applied"); break;
        }
        if (spacing is null)
        {
            var ls = Regex.Match(pre, @"\\linespread\s*\{\s*([0-9.]+)\s*\}");
            if (ls.Success && double.TryParse(ls.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lv))
                // lineSpacing 1.5 and 2 mean setspace's \onehalfspacing and \doublespacing (about 1.25 and 1.67 as
                // a \linespread factor), so \linespread{2} stored as 2 came back much tighter. A factor that lands
                // on those two values is nudged off them, and the builder writes it back as \linespread{2}.
                spacing = Math.Abs(lv - 1.5) < 1e-9 || Math.Abs(lv - 2.0) < 1e-9 ? lv + 1e-6 : lv;
        }
        if (spacing is { } sp) Set("lineSpacing", i => i with { LineSpacing = sp }, sp.ToString(CultureInfo.InvariantCulture));

        // ── paragraph indent ───────────────────────────────────────────────
        var pi = Regex.Match(pre, @"\\setlength\s*\{?\\parindent\}?\s*\{\s*([^}]+?)\s*\}")
            is { Success: true } m1 ? m1
            : Regex.Match(pre, @"\\parindent\s*=?\s*([0-9.]+\s*[a-z]{2})");
        if (pi.Success)
        {
            var raw = pi.Groups[1].Value.Replace(" ", "");
            var indent = Regex.IsMatch(raw, @"^0(\.0+)?(pt|cm|mm|em|in|bp)?$") ? "none" : raw;
            Set("paragraphIndent", i => i with { ParagraphIndent = indent }, indent);
        }
        if (Regex.IsMatch(pre, @"\\usepackage(?:\[[^\]]*\])?\{[^}]*\bparskip\b[^}]*\}"))
            notes.Add("parskip (paragraph spacing instead of indent) is not supported");

        // ── page numbering ─────────────────────────────────────────────────
        // The LAST \pagenumbering: a thesis numbers its front matter in roman and switches to arabic for the
        // body, and the body is what the document is. The first match made the whole thesis roman (5 Oct review).
        var pn = Regex.Matches(text, @"\\pagenumbering\s*\{\s*([A-Za-z]+)\s*\}").LastOrDefault() ?? Match.Empty;
        if (pn.Success)
        {
            var kind = pn.Groups[1].Value;
            if (kind is "arabic" or "roman") Set("pageNumbering", i => i with { PageNumbering = kind }, kind);
            else notes.Add($"\\pagenumbering{{{kind}}} not applied (arabic, roman or none)");
        }
        else if (Regex.IsMatch(pre, @"\\pagestyle\s*\{\s*empty\s*\}"))
            Set("pageNumbering", i => i with { PageNumbering = "none" }, "none");

        // ── running headers and footers ────────────────────────────────────
        var slots = new Dictionary<string, string>();
        var dynamic = new List<string>();
        foreach (var (slotName, raw) in FindHeaderFooter(pre, notes))
        {
            var plain = PlainText(raw, out var dropped);
            if (dropped.Count > 0) dynamic.Add($"{slotName} ({string.Join(", ", dropped)})");
            if (plain.Length > 0) slots[slotName] = plain;
        }
        foreach (var (name, val) in slots)
        {
            var v = val;
            switch (name)
            {
                case "headerLeft": Set(name, i => i with { HeaderLeft = v }, $"\"{v}\""); break;
                case "headerCenter": Set(name, i => i with { HeaderCenter = v }, $"\"{v}\""); break;
                case "headerRight": Set(name, i => i with { HeaderRight = v }, $"\"{v}\""); break;
                case "footerLeft": Set(name, i => i with { FooterLeft = v }, $"\"{v}\""); break;
                case "footerCenter": Set(name, i => i with { FooterCenter = v }, $"\"{v}\""); break;
                case "footerRight": Set(name, i => i with { FooterRight = v }, $"\"{v}\""); break;
            }
        }
        // What Lilia prints (LaTeXPreambleBuilder): with no footer slot set it keeps the page number
        // centred at the foot; a footer slot replaces it. These notes said a header alone removed it,
        // which the \cfoot{\thepage} rule made false (5 Oct review).
        var footerSlotSet = slots.Keys.Any(k => k.StartsWith("footer", StringComparison.Ordinal));
        if (dynamic.Count > 0)
            notes.Add("header/footer commands dropped, Lilia's slots are plain text: " + string.Join("; ", dynamic)
                + (footerSlotSet
                    ? ". A page number in a footer is not reproduced, because a footer slot is set."
                    : ". A page number in the footer is still printed: Lilia puts it centred at the foot."));
        else if (footerSlotSet)
            notes.Add("with a footer slot set, Lilia does not print the automatic page number");

        // ── columns ────────────────────────────────────────────────────────
        var gap = Regex.Match(pre, @"\\setlength\s*\{?\\columnsep\}?\s*\{\s*([0-9.]+)\s*(cm|mm|pt|in|em)\s*\}");
        if (gap.Success)
        {
            var cm = ToCentimetres(gap.Groups[1].Value, gap.Groups[2].Value);
            Set("columnGap", i => i with { ColumnGap = cm }, $"{cm.ToString(CultureInfo.InvariantCulture)}cm");
        }
        var rule = Regex.Match(pre, @"\\setlength\s*\{?\\columnseprule\}?\s*\{\s*([0-9.]+)");
        if (rule.Success && double.TryParse(rule.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rw) && rw > 0)
            Set("columnSeparator", i => i with { ColumnSeparator = "rule" }, "rule");
        if (Regex.IsMatch(text, @"\\begin\{multicols\*?\}"))
            notes.Add("multicols environments in the body are not converted (use the columns setting for the whole document)");

        // ── fonts ──────────────────────────────────────────────────────────
        var pkgs = new HashSet<string>(meta.Packages.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        string? family = null;
        foreach (var (pkg, fam) in new[]
        {
            ("times", "times"), ("mathptmx", "times"), ("newtxtext", "times"),
            ("palatino", "palatino"), ("mathpazo", "palatino"), ("newpxtext", "palatino"),
            ("charter", "charter"), ("xcharter", "charter"), ("bookman", "bookman"),
        })
            if (pkgs.Contains(pkg)) { family = fam; break; }
        if (family is null && Regex.IsMatch(pre, @"\\renewcommand\s*\{?\\familydefault\}?\s*\{\\sfdefault\}")) family = "sans-serif";
        if (family is null && Regex.IsMatch(pre, @"\\renewcommand\s*\{?\\familydefault\}?\s*\{\\ttdefault\}")) family = "monospace";
        if (family is not null) Set("fontFamily", i => i with { FontFamily = family }, family);
        var unsupportedFonts = pkgs.Where(p => p.Equals("fontspec", StringComparison.OrdinalIgnoreCase)
            || p.Equals("libertine", StringComparison.OrdinalIgnoreCase) || p.Equals("libertinus", StringComparison.OrdinalIgnoreCase)
            || p.Equals("cmbright", StringComparison.OrdinalIgnoreCase)).ToList();
        if (unsupportedFonts.Count > 0 || Regex.IsMatch(pre, @"\\set(main|sans|mono)font"))
            notes.Add("fonts not applied: arbitrary and OpenType fonts (fontspec, \\setmainfont" +
                (unsupportedFonts.Count > 0 ? "; " + string.Join(", ", unsupportedFonts) : "") + ") are not supported");

        // ── other things Lilia cannot do ───────────────────────────────────
        if (pkgs.Contains("draftwatermark") || pkgs.Contains("watermark") || pkgs.Contains("background"))
            notes.Add("watermark not applied (Lilia has no watermark)");
        if (pkgs.Contains("titlesec")) notes.Add("titlesec heading restyling not applied");

        // Class, packages and macros exactly as the importer keeps them.
        var pre2 = LatexPreambleExtractor.Extract(tex);
        if (!string.IsNullOrWhiteSpace(pre2.CustomPreamble))
        {
            var cp = pre2.CustomPreamble!;
            Set("customPreamble", i => i with { CustomPreamble = cp }, $"{cp.Split('\n').Length} line(s) of macros");
        }
        if (pre2.DocumentClass is not null) applied.Add($"documentClass = {pre2.DocumentClass}");
        if (pre2.PackagesJson is not null) applied.Add("packages = the \\usepackage list");

        return new Result(input, pre2.DocumentClass, pre2.PackagesJson, twoside ? "two" : null, titlePage, applied, notes);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static double ToCentimetres(string n, string unit)
    {
        var v = double.Parse(n, CultureInfo.InvariantCulture);
        var cm = unit switch { "mm" => v / 10, "pt" => v * 0.03528, "in" => v * 2.54, "em" => v * 0.42, _ => v };
        return Math.Round(cm, 2);
    }

    /// <summary>Split "a=1,b={2,3},c" at top-level commas.</summary>
    internal static IEnumerable<string> SplitTopLevel(string s)
    {
        var depth = 0; var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch == '{') depth++;
            if (ch == '}') depth--;
            if (ch == ',' && depth == 0) { if (sb.Length > 0) yield return sb.ToString().Trim(); sb.Clear(); continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    /// <summary>The balanced {...} group starting at <paramref name="open"/>.</summary>
    internal static string? ReadBraced(string s, int open, out int end)
    {
        end = open;
        if (open >= s.Length || s[open] != '{') return null;
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') depth++;
            else if (s[i] == '}') { depth--; if (depth == 0) { end = i; return s[(open + 1)..i]; } }
        }
        return null;
    }

    private static readonly Regex HfCmd = new(
        @"\\(?<cmd>lhead|chead|rhead|lfoot|cfoot|rfoot|fancyhead|fancyfoot)\s*(?:\[(?<opt>[^\]]*)\])?\s*(?=\{)",
        RegexOptions.Compiled);

    /// <summary>(slot name, raw text) for every header/footer command in the source.</summary>
    internal static List<(string Slot, string Raw)> FindHeaderFooter(string pre, List<string> notes)
    {
        var found = new List<(string, string)>();
        var evenOnly = false; var unqualified = false;
        foreach (Match m in HfCmd.Matches(pre))
        {
            var body = ReadBraced(pre, m.Index + m.Length, out _);
            if (body is null || body.Trim().Length == 0) continue;
            var cmd = m.Groups["cmd"].Value;
            var isHead = cmd.EndsWith("head", StringComparison.Ordinal);
            if (cmd is "lhead" or "lfoot") { found.Add((isHead ? "headerLeft" : "footerLeft", body)); continue; }
            if (cmd is "chead" or "cfoot") { found.Add((isHead ? "headerCenter" : "footerCenter", body)); continue; }
            if (cmd is "rhead" or "rfoot") { found.Add((isHead ? "headerRight" : "footerRight", body)); continue; }
            // \fancyhead[L,RO]{...}
            if (!m.Groups["opt"].Success) { unqualified = true; continue; }
            foreach (var tok in m.Groups["opt"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var t = tok.ToUpperInvariant();
                if (t.Contains('E') && !t.Contains('O')) { evenOnly = true; continue; } // odd pages win
                var side = t.Contains('L') ? "Left" : t.Contains('C') ? "Center" : t.Contains('R') ? "Right" : null;
                if (side is null) continue;
                found.Add(((isHead ? "header" : "footer") + side, body));
            }
        }
        if (evenOnly) notes.Add("separate even-page headers/footers not applied (Lilia uses one for all pages)");
        if (unqualified) notes.Add("\\fancyhead/\\fancyfoot without [L], [C] or [R] not applied");
        return found;
    }

    /// <summary>
    /// A header/footer's text with LaTeX taken out. Returns what was dropped,
    /// because <c>\thepage</c> and <c>\leftmark</c> mean something the plain
    /// slot cannot.
    /// </summary>
    internal static string PlainText(string raw, out List<string> dropped)
    {
        dropped = new List<string>();
        var s = raw;
        foreach (var dyn in new[] { "thepage", "leftmark", "rightmark", "thesection", "thechapter", "today", "nouppercase" })
            if (Regex.IsMatch(s, @"\\" + dyn + @"(?![A-Za-z])")) { dropped.Add("\\" + dyn); s = Regex.Replace(s, @"\\" + dyn + @"(?![A-Za-z])", ""); }
        s = Regex.Replace(s, @"\\(hfill|quad|qquad|,|;|:|!| )", " ");
        for (var i = 0; i < 4; i++)
            s = Regex.Replace(s, @"\\[A-Za-z]+\*?\s*\{([^{}]*)\}", "$1");
        var left = Regex.Matches(s, @"\\[A-Za-z]+\*?");
        foreach (Match c in left) if (!dropped.Contains(c.Value)) dropped.Add(c.Value);
        s = Regex.Replace(s, @"\\[A-Za-z]+\*?", "");
        s = s.Replace(@"\\", " ").Replace(@"\&", "&").Replace(@"\%", "%").Replace(@"\_", "_").Replace(@"\#", "#").Replace(@"\$", "$")
             .Replace("~", " ").Replace("{", "").Replace("}", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
