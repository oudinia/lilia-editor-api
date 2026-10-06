using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// What a table's columns MEAN, as opposed to how a paper draws them (Olivia's 2g, document
/// themes: "the table owns meaning, the paper owns the look"): decimal alignment, a unit in the
/// header, and the best value in bold. All three travel with the table into every paper and into
/// Copy LaTeX, so the LaTeX they produce is the same under every theme.
/// </summary>
/// <remarks>
/// <para><b>Stored</b> (in the table content, beside <c>columnAlign</c>, one entry per column):</para>
/// <list type="bullet">
/// <item><c>columnAlign[i] = "decimal"</c>: an siunitx <c>S</c> column, aligned on the decimal point.</item>
/// <item><c>columnUnit[i]</c>: a unit string. The header prints <c>Top-1 (\%)</c> and the unit is
/// stripped from body cells that repeat it.</item>
/// <item><c>columnBest[i] = "higher" | "lower"</c>: the best number in the column is bold.</item>
/// </list>
///
/// <para><b>Derived at emit time, never stored:</b> which cells are best, the S column's
/// <c>table-format</c>, and <b>automatic decimal alignment</b>. A column is decimal-aligned
/// automatically when the author has not chosen its alignment — <c>columnAlign</c> is absent,
/// empty, <c>"auto"</c> or the default <c>"l"</c> — and its body holds at least two numbers and
/// nothing but numbers (blank cells and dashes aside). <c>"l"</c> counts as "not chosen" because
/// every table the tool has ever saved stores it for a column nobody touched; an author who wants
/// a column of numbers out of the S column picks centre or right, which win. One number has
/// nothing to line up with, so a column needs two.</para>
///
/// <para>Merged cells turn the automatic part off for the whole table: a span moves cells between
/// columns, and a guess about which numbers line up is not worth a wrong one. An explicit
/// <c>decimal</c> is still honoured there.</para>
///
/// <para>The client mirrors this in <c>table-grid/tableFormat.ts</c>; change both together.</para>
/// </remarks>
public static class TableColumnFormat
{
    public enum BestDirection { None, Higher, Lower }

    /// <summary>A body cell that reads as a number.</summary>
    /// <param name="Sign"><c>"-"</c>, <c>"+"</c> (written by the author) or empty.</param>
    /// <param name="Int">Integer digits, separators removed (may be empty for <c>.5</c>).</param>
    /// <param name="Frac">Fraction digits; empty when there is none.</param>
    /// <param name="HasPoint">Whether a decimal point was written.</param>
    /// <param name="Grouped">Whether the integer part was written with thousands commas.</param>
    /// <param name="Percent">Whether a trailing <c>%</c> was written.</param>
    /// <param name="Bold">Whether the author wrapped the whole cell in <c>\textbf</c>.</param>
    public sealed record Number(string Sign, string Int, string Frac, bool HasPoint, bool Grouped, bool Percent, bool Bold, double Value);

    // sign · integer (plain, or comma-grouped in threes) · fraction · optional %.
    private static readonly Regex NumberRx = new(
        @"^(?<sign>[+\-−–])?\s*(?<int>\d{1,3}(?:,\d{3})+|\d+)?(?:\.(?<frac>\d+))?\s*(?<pct>%)?$",
        RegexOptions.CultureInvariant);

    /// <summary>Cells that hold no value: empty, or a dash standing for one.</summary>
    public static bool IsBlank(string? raw)
    {
        var t = (raw ?? "").Trim();
        return t.Length == 0 || t is "-" or "--" or "---" or "–" or "—";
    }

    /// <summary>
    /// Read a cell as a number: <c>76.1</c>, <c>−1.4</c>, <c>+2.7</c>, <c>1,204</c>, <c>50.7%</c>,
    /// <c>$+2.7$</c> and <c>\textbf{94.8}</c>. Anything else — exponents, <c>\pm</c>, words — is not.
    /// </summary>
    public static Number? ParseNumber(string? raw)
    {
        var t = (raw ?? "").Trim();
        var bold = false;
        if (LatexText.IsWhollyBold(t))
        {
            bold = true;
            t = t["\\textbf{".Length..^1].Trim();
        }
        if (LatexText.IsWhollyMaths(t)) t = t[1..^1].Trim();
        if (t.Length == 0) return null;

        var m = NumberRx.Match(t);
        if (!m.Success) return null;
        var intRaw = m.Groups["int"].Value;
        var frac = m.Groups["frac"].Value;
        if (intRaw.Length == 0 && frac.Length == 0) return null;

        var signCh = m.Groups["sign"].Value;
        var sign = signCh.Length == 0 ? "" : signCh == "+" ? "+" : "-";
        var digits = intRaw.Replace(",", "");
        var value = double.Parse((digits.Length == 0 ? "0" : digits) + (frac.Length > 0 ? "." + frac : ""), CultureInfo.InvariantCulture);
        if (sign == "-") value = -value;
        return new Number(sign, digits, frac, t.Contains('.'), intRaw.Contains(','), m.Groups["pct"].Success, bold, value);
    }

    /// <summary>
    /// The cell without a trailing <paramref name="unit"/>, when what is left is a number
    /// (<c>12 ms</c> → <c>12</c>). A cell that merely ends in the same letters (<c>items</c>
    /// for <c>ms</c>) is left alone.
    /// </summary>
    public static string StripUnit(string raw, string? unit)
    {
        if (string.IsNullOrEmpty(unit)) return raw;
        var t = raw.Trim();
        if (!t.EndsWith(unit, StringComparison.Ordinal)) return raw;
        var rest = t[..^unit.Length].TrimEnd();
        return rest.Length > 0 && ParseNumber(rest) is not null ? rest : raw;
    }

    /// <summary>
    /// The header with its unit: <c>Top-1</c> + <c>%</c> → <c>Top-1 (%)</c>. A header that already
    /// ends in the unit in brackets is left as it is.
    /// </summary>
    public static string HeaderWithUnit(string header, string? unit)
    {
        if (string.IsNullOrEmpty(unit)) return header;
        var h = header.TrimEnd();
        if (h.EndsWith($"({unit})", StringComparison.Ordinal) || h.EndsWith($"[{unit}]", StringComparison.Ordinal)) return header;
        return h.Length == 0 ? $"({unit})" : $"{h} ({unit})";
    }

    /// <summary>A number as an S column reads it, with <c>\bfseries</c> in front when bold.</summary>
    public static string NumberLatex(Number n, bool bold)
    {
        var sb = new StringBuilder();
        if (bold) sb.Append(@"\bfseries ");
        sb.Append(n.Sign).Append(n.Int);
        if (n.HasPoint) sb.Append('.').Append(n.Frac);
        if (n.Percent) sb.Append(@"{\%}");
        return sb.ToString();
    }

    /// <summary>A table cell's text: a plain string or an object <c>{content|text, colspan, rowspan}</c>.</summary>
    public static string CellText(JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.String) return cell.GetString() ?? "";
        if (cell.ValueKind == JsonValueKind.Object)
        {
            if (cell.TryGetProperty("content", out var ct) && ct.ValueKind == JsonValueKind.String) return ct.GetString() ?? "";
            if (cell.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String) return tx.GetString() ?? "";
        }
        return "";
    }

    private static bool Spanned(JsonElement cell)
    {
        if (cell.ValueKind != JsonValueKind.Object) return false;
        static int Get(JsonElement c, string p) => c.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 1;
        return Get(cell, "colspan") > 1 || Get(cell, "rowspan") > 1;
    }

    private static string? StringAt(JsonElement content, string prop, int i)
    {
        if (!content.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array || i >= arr.GetArrayLength()) return null;
        var el = arr[i];
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    /// <summary>Plan a table's columns. Pass the column count the emitter uses.</summary>
    public static TablePlan Plan(JsonElement content, int colCount, bool hasHeaders)
    {
        var rows = content.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList()
            : [];
        var hasSpans = rows.Any(row => row.ValueKind == JsonValueKind.Array && row.EnumerateArray().Any(Spanned))
            || (content.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Array && hs.EnumerateArray().Any(Spanned));
        var alignProp = content.TryGetProperty("columnAlign", out var ca) && ca.ValueKind == JsonValueKind.Array ? "columnAlign" : "alignments";

        var columns = new ColumnPlan[Math.Max(0, colCount)];
        for (var c = 0; c < columns.Length; c++)
        {
            var align = (StringAt(content, alignProp, c) ?? "").Trim().ToLowerInvariant();
            var explicitDecimal = align is "decimal" or "d" or "s";
            var auto = align is "" or "l" or "left" or "auto";
            var unitRaw = StringAt(content, "columnUnit", c)?.Trim();
            // A unit moves into the header, so without a header there is nowhere for it to go.
            var unit = hasHeaders && !string.IsNullOrEmpty(unitRaw) ? unitRaw : null;
            var best = (StringAt(content, "columnBest", c) ?? "").Trim().ToLowerInvariant() switch
            {
                "higher" or "high" or "max" => BestDirection.Higher,
                "lower" or "low" or "min" => BestDirection.Lower,
                _ => BestDirection.None,
            };

            var numbers = new Dictionary<int, Number>();
            var others = 0;
            for (var ri = 0; ri < rows.Count; ri++)
            {
                if (rows[ri].ValueKind != JsonValueKind.Array || c >= rows[ri].GetArrayLength()) continue;
                var cell = rows[ri][c];
                var text = StripUnit(CellText(cell), unit);
                if (IsBlank(text)) continue;
                var n = Spanned(cell) ? null : ParseNumber(text);
                if (n is null) others++;
                else numbers[ri] = n;
            }

            var isDecimal = explicitDecimal || (auto && !hasSpans && others == 0 && numbers.Count >= 2);

            // The best value, as a value: every cell holding it is bold, so a tie bolds them all.
            // Positions are not trusted across merged cells, so a table with spans has none.
            double? bestValue = null;
            if (best != BestDirection.None && !hasSpans && numbers.Count >= 2)
            {
                var target = best == BestDirection.Higher ? numbers.Values.Max(n => n.Value) : numbers.Values.Min(n => n.Value);
                // Every value equal: nothing is best.
                if (numbers.Values.Any(n => n.Value != target)) bestValue = target;
            }

            columns[c] = new ColumnPlan(c, isDecimal, !explicitDecimal && isDecimal, unit, best,
                // A span moves cells between columns, so their widths are not this column's.
                hasSpans ? [] : numbers.Values.ToList(), bestValue, numbers.Values.Any(n => n.Bold));
        }
        return new TablePlan(columns);
    }

    /// <summary>One column's plan.</summary>
    public sealed class ColumnPlan
    {
        internal ColumnPlan(int index, bool isDecimal, bool automatic, string? unit, BestDirection best,
            List<Number> numbers, double? bestValue, bool authorBold)
        {
            Index = index;
            IsDecimal = isDecimal;
            Automatic = automatic;
            Unit = unit;
            Best = best;
            Numbers = numbers;
            BestValue = bestValue;
            _authorBold = authorBold;
        }

        private readonly bool _authorBold;

        public int Index { get; }
        /// <summary>An siunitx S column.</summary>
        public bool IsDecimal { get; }
        /// <summary>Decimal because every value is a number, not because the author chose it.</summary>
        public bool Automatic { get; }
        public string? Unit { get; }
        public BestDirection Best { get; }
        /// <summary>The body's numbers (empty in a table with merged cells).</summary>
        public IReadOnlyList<Number> Numbers { get; }
        /// <summary>The best value; every cell holding it is bold. Null when there is none.</summary>
        public double? BestValue { get; }

        /// <summary>Whether a number in this cell is the best one.</summary>
        public bool IsBest(Number? n) => n is not null && BestValue is { } b && n.Value == b;

        /// <summary>
        /// <c>S[table-format=-2.2, mode=text, …]</c>. The format is the widest integer and fraction
        /// in the column, with room for a sign when one is written.
        /// </summary>
        /// <remarks>
        /// <para><c>mode=text</c> sets the digits in the paper's text face, as every other number in
        /// the table and the prose is — without it they come out in the maths face, which under a
        /// themed paper is a different typeface.</para>
        /// <para><c>reset-text-series=false</c> is siunitx 3's form of the old
        /// <c>detect-weight</c> (which siunitx 3 still accepts, with a deprecation warning): it
        /// lets a <c>\bfseries</c> in the cell reach the number. Only written when the column has
        /// a bold value.</para>
        /// <para>Grouping follows what the author wrote: commas kept as commas
        /// (<c>1,204</c>), and a long number written without them is not given siunitx's thin
        /// spaces either.</para>
        /// <para>Without numbers to measure (a table with merged cells) there is no
        /// <c>table-format</c>, and siunitx centres the column on the decimal marker itself.</para>
        /// </remarks>
        public string Spec()
        {
            var opts = new List<string>();
            if (Numbers.Count > 0)
            {
                var ints = Numbers.Max(n => Math.Max(1, n.Int.Length));
                var fracs = Numbers.Max(n => n.Frac.Length);
                var sign = Numbers.Any(n => n.Sign.Length > 0) ? "-" : "";
                opts.Add($"table-format={sign}{ints}" + (fracs > 0 ? $".{fracs}" : ""));
            }
            opts.Add("mode=text");
            if (Numbers.Any(n => n.Sign == "+")) opts.Add("retain-explicit-plus");
            if (Numbers.Any(n => n.Grouped))
                opts.Add("group-digits=integer, group-separator={,}, group-minimum-digits=4");
            else if (Numbers.Any(n => n.Int.Length >= 5 || n.Frac.Length >= 5))
                opts.Add("group-digits=none");
            if (BestValue is not null || _authorBold) opts.Add("reset-text-series=false");
            return $"S[{string.Join(", ", opts)}]";
        }
    }

    /// <summary>Every column's plan, and the helpers the LaTeX emitters share.</summary>
    public sealed class TablePlan
    {
        internal TablePlan(ColumnPlan[] columns) => Columns = columns;

        public IReadOnlyList<ColumnPlan> Columns { get; }

        private ColumnPlan? At(int c) => c >= 0 && c < Columns.Count ? Columns[c] : null;

        public bool IsDecimal(int c) => At(c)?.IsDecimal == true;

        public bool AnyDecimal => Columns.Any(c => c.IsDecimal);

        /// <summary>The header's text with the column's unit added.</summary>
        public string HeaderText(int c, string header) => HeaderWithUnit(header, At(c)?.Unit);

        /// <summary>The body cell's text with the column's unit stripped.</summary>
        public string BodyText(int c, string text) => StripUnit(text, At(c)?.Unit);

        /// <summary>Whether this body text is the column's best value.</summary>
        public bool IsBest(int c, string bodyText) => At(c)?.IsBest(ParseNumber(bodyText)) == true;

        /// <summary>
        /// The S column's number for this body text (unit already stripped), or null when it is
        /// not a number in a decimal column — the emitter escapes it and <see cref="Protect"/>
        /// braces it.
        /// </summary>
        public string? DecimalNumber(int c, string bodyText)
        {
            var col = At(c);
            if (col is null || !col.IsDecimal) return null;
            var n = ParseNumber(bodyText);
            return n is null ? null : NumberLatex(n, n.Bold || col.IsBest(n));
        }

        /// <summary>
        /// A rendered cell made safe for an S column: siunitx reads anything not in braces as a
        /// number, so text (a header, a dash, a word) goes in braces. A <c>\multicolumn</c> replaces
        /// the column type and must come first in its cell, so it is left as it is.
        /// </summary>
        public string Protect(int c, string rendered)
        {
            if (!IsDecimal(c) || rendered.Length == 0) return rendered;
            if (rendered.StartsWith(@"\multicolumn", StringComparison.Ordinal)) return rendered;
            return "{" + rendered + "}";
        }

        /// <summary>A best cell outside an S column, bolded once.</summary>
        public string BoldBest(int c, string bodyText, string rendered) =>
            !IsDecimal(c) && IsBest(c, bodyText) && rendered.Trim().Length > 0 && !LatexText.IsWhollyBold(rendered)
                ? $@"\textbf{{{rendered}}}"
                : rendered;

        /// <summary>The column spec with each decimal column's S in place of its letter.</summary>
        public string ApplyToSpec(IReadOnlyList<string> letters)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < letters.Count; i++)
                sb.Append(IsDecimal(i) ? Columns[i].Spec() : letters[i]);
            return sb.ToString();
        }
    }
}
