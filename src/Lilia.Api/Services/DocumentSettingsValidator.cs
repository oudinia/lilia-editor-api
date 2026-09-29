using System.Globalization;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

/// <summary>
/// What the Document Settings can hold, and the check for a proposed change.
///
/// <para>The dialog constrains most fields by construction (selects and
/// toggles) and the update endpoint stores whatever it is sent. Values that are
/// typed free text reach the LaTeX unchecked: a margin goes straight into
/// <c>\usepackage[top=...]{geometry}</c>, a paragraph indent into
/// <c>\setlength</c>. Ask Lilia's model types those, so this is the gate in
/// front of them; the same rules can guard the endpoint.</para>
/// </summary>
public static class DocumentSettingsValidator
{
    public static readonly string[] PaperSizes = { "a4", "letter", "legal", "a5", "executive", "b5" };
    public static readonly string[] Orientations = { "portrait", "landscape" };
    public static readonly string[] FontFamilies = { "serif", "sans-serif", "monospace", "charter", "times", "palatino", "bookman" };
    public static readonly int[] FontSizes = { 10, 11, 12 };
    public static readonly string[] PageNumberings = { "arabic", "roman", "none" };
    public static readonly string[] ColumnSeparators = { "none", "rule" };
    public const int MaxColumns = 3;
    public const double MinLineSpacing = 0.8, MaxLineSpacing = 3.0;
    public const double MaxColumnGapCm = 5.0;
    public const double MaxLengthMm = 100.0;
    public const int MaxSlotChars = 200;
    public const int MaxPreambleChars = 20000;

    private static readonly Regex Length = new(
        @"^(\d+(?:\.\d+)?)(in|cm|mm|pt|bp|em)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The fields a caller may change. Null means "leave alone".</summary>
    public sealed record Input(
        string? PaperSize = null, string? Orientation = null,
        string? MarginTop = null, string? MarginBottom = null, string? MarginLeft = null, string? MarginRight = null,
        int? Columns = null, double? ColumnGap = null, string? ColumnSeparator = null, bool? BalancedColumns = null,
        string? FontFamily = null, int? FontSize = null, double? LineSpacing = null, string? ParagraphIndent = null,
        string? PageNumbering = null,
        string? HeaderLeft = null, string? HeaderCenter = null, string? HeaderRight = null,
        string? FooterLeft = null, string? FooterCenter = null, string? FooterRight = null,
        string? CustomPreamble = null);

    public sealed record Result(UpdateDocumentDto? Update, IReadOnlyList<string> Errors, IReadOnlyList<string> Fields)
    {
        public bool Ok => Errors.Count == 0;
    }

    public static Result Validate(Input i)
    {
        var errors = new List<string>();
        var fields = new List<string>();
        void Touch(string name) => fields.Add(name);

        string? paper = null, orient = null, family = null, pn = null, sep = null;
        if (i.PaperSize is not null)
        {
            paper = i.PaperSize.Trim().ToLowerInvariant();
            if (!PaperSizes.Contains(paper)) errors.Add($"paperSize '{i.PaperSize}' is not valid. Valid: {string.Join(", ", PaperSizes)}.");
            Touch("paperSize");
        }
        if (i.Orientation is not null)
        {
            orient = i.Orientation.Trim().ToLowerInvariant();
            if (!Orientations.Contains(orient)) errors.Add($"orientation '{i.Orientation}' is not valid. Valid: {string.Join(", ", Orientations)}.");
            Touch("orientation");
        }
        string? Margin(string? raw, string name)
        {
            if (raw is null) return null;
            Touch(name);
            var v = raw.Trim().ToLowerInvariant();
            if (v.Length == 0) return ""; // clears the margin back to the class default
            var m = Length.Match(v);
            if (!m.Success)
            {
                errors.Add($"{name} '{raw}' is not valid. Use a number with a unit, e.g. 2.5cm, 1in, 20mm, 72pt (units: in, cm, mm, pt, bp, em), or \"\" to reset.");
                return null;
            }
            if (ToMillimetres(m) > MaxLengthMm)
                errors.Add($"{name} '{raw}' is too large (maximum about {MaxLengthMm:0} mm / 10 cm).");
            return v;
        }
        var mt = Margin(i.MarginTop, "marginTop");
        var mb = Margin(i.MarginBottom, "marginBottom");
        var ml = Margin(i.MarginLeft, "marginLeft");
        var mr = Margin(i.MarginRight, "marginRight");

        if (i.Columns is { } c)
        {
            if (c < 1 || c > MaxColumns) errors.Add($"columns {c} is not valid. Valid: 1 to {MaxColumns}.");
            Touch("columns");
        }
        if (i.ColumnGap is { } g)
        {
            if (double.IsNaN(g) || g < 0 || g > MaxColumnGapCm) errors.Add($"columnGap {g.ToString(CultureInfo.InvariantCulture)} is not valid. It is in centimetres: 0 to {MaxColumnGapCm:0}.");
            Touch("columnGap");
        }
        if (i.ColumnSeparator is not null)
        {
            var s = i.ColumnSeparator.Trim().ToLowerInvariant();
            sep = s is "line" or "rule" ? "rule" : s;
            if (!ColumnSeparators.Contains(sep)) errors.Add($"columnSeparator '{i.ColumnSeparator}' is not valid. Valid: none, rule (a vertical rule between columns).");
            Touch("columnSeparator");
        }
        if (i.BalancedColumns is not null) Touch("balancedColumns");

        if (i.FontFamily is not null)
        {
            family = i.FontFamily.Trim().ToLowerInvariant();
            if (!FontFamilies.Contains(family)) errors.Add($"fontFamily '{i.FontFamily}' is not valid. Valid: {string.Join(", ", FontFamilies)}. Arbitrary fonts are not supported.");
            Touch("fontFamily");
        }
        if (i.FontSize is { } fs)
        {
            if (!FontSizes.Contains(fs)) errors.Add($"fontSize {fs} is not valid. Valid (pt): {string.Join(", ", FontSizes)}.");
            Touch("fontSize");
        }
        if (i.LineSpacing is { } ls)
        {
            if (double.IsNaN(ls) || ls < MinLineSpacing || ls > MaxLineSpacing)
                errors.Add($"lineSpacing {ls.ToString(CultureInfo.InvariantCulture)} is not valid. Valid: {MinLineSpacing} to {MaxLineSpacing} (1 single, 1.5 one-and-a-half, 2 double).");
            Touch("lineSpacing");
        }
        string? indent = null;
        if (i.ParagraphIndent is not null)
        {
            Touch("paragraphIndent");
            indent = i.ParagraphIndent.Trim().ToLowerInvariant();
            if (indent.Length > 0 && indent != "none")
            {
                var m = Length.Match(indent);
                if (!m.Success) errors.Add($"paragraphIndent '{i.ParagraphIndent}' is not valid. Use a length such as 1.5em, 1cm, 12pt, or \"none\" for no indent.");
                else if (ToMillimetres(m) > MaxLengthMm) errors.Add($"paragraphIndent '{i.ParagraphIndent}' is too large.");
            }
        }
        if (i.PageNumbering is not null)
        {
            pn = i.PageNumbering.Trim().ToLowerInvariant();
            if (!PageNumberings.Contains(pn)) errors.Add($"pageNumbering '{i.PageNumbering}' is not valid. Valid: {string.Join(", ", PageNumberings)}.");
            Touch("pageNumbering");
        }

        string? Slot(string? raw, string name)
        {
            if (raw is null) return null;
            Touch(name);
            if (raw.Length > MaxSlotChars) { errors.Add($"{name} is {raw.Length} characters; the limit is {MaxSlotChars}."); return null; }
            if (raw.IndexOfAny(new[] { '\r', '\n' }) >= 0) { errors.Add($"{name} must be a single line."); return null; }
            return raw.Trim();
        }
        var hl = Slot(i.HeaderLeft, "headerLeft");
        var hc = Slot(i.HeaderCenter, "headerCenter");
        var hr = Slot(i.HeaderRight, "headerRight");
        var fl = Slot(i.FooterLeft, "footerLeft");
        var fc = Slot(i.FooterCenter, "footerCenter");
        var fr = Slot(i.FooterRight, "footerRight");

        string? preamble = null;
        if (i.CustomPreamble is not null)
        {
            Touch("customPreamble");
            if (i.CustomPreamble.Length > MaxPreambleChars) errors.Add($"customPreamble is {i.CustomPreamble.Length} characters; the limit is {MaxPreambleChars}.");
            else if (i.CustomPreamble.Contains('\0')) errors.Add("customPreamble contains a NUL character.");
            else preamble = i.CustomPreamble;
        }

        if (errors.Count > 0) return new Result(null, errors, fields);

        var dto = new UpdateDocumentDto(
            Title: null, Language: null, PaperSize: paper, FontFamily: family, FontSize: i.FontSize,
            Columns: i.Columns, ColumnSeparator: sep, ColumnGap: i.ColumnGap,
            LatexDocumentClass: null, LatexDocumentClassOptions: null, LatexPackages: null,
            BalancedColumns: i.BalancedColumns,
            MarginTop: mt, MarginBottom: mb, MarginLeft: ml, MarginRight: mr,
            HeaderText: null, FooterText: null,
            LineSpacing: i.LineSpacing, ParagraphIndent: indent, PageNumbering: pn,
            AiEnabled: null, LatexEngine: null, ExperimentalLatexEdit: null,
            DocumentClass: null, DocumentCategory: null, Sides: null, TitlePage: null,
            Orientation: orient, CustomPreamble: preamble,
            HeaderLeft: hl, HeaderCenter: hc, HeaderRight: hr,
            FooterLeft: fl, FooterCenter: fc, FooterRight: fr);
        return new Result(dto, errors, fields);
    }

    private static double ToMillimetres(Match m)
    {
        var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value switch
        {
            "in" => n * 25.4,
            "cm" => n * 10,
            "mm" => n,
            "pt" or "bp" => n * 0.3528,
            "em" => n * 4.2, // about a 12 pt em
            _ => n,
        };
    }
}
