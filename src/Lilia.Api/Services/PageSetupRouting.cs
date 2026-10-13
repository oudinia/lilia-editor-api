using Lilia.Core.Entities;

namespace Lilia.Api.Services;

/// <summary>
/// Whether a document's page setup can only be honoured by LaTeX.
///
/// <para>The preview and the default PDF export try Typst first (fast) for documents up to 500
/// blocks. The Typst export applies the paper size, the font family and the font size, and nothing
/// else of the page setup: margins, orientation, columns, line spacing, indent, page numbering,
/// running headers and footers, the document class and its options, and the custom preamble are
/// all ignored. A document that sets any of them got a PDF that did not look like its settings,
/// while Ask Lilia's page-setup tools and the Document Settings dialog said they were applied
/// (5 Oct review). Such a document now goes to LaTeX, which honours all of them.</para>
///
/// <para>Plain documents (the default page setup) keep the Typst path and its speed.</para>
/// </summary>
public static class PageSetupRouting
{
    /// <summary>Null when Typst can render the page setup faithfully, else what it would ignore.</summary>
    public static string? WhyLatex(Document d)
    {
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.MarginTop) || !string.IsNullOrWhiteSpace(d.MarginBottom)
            || !string.IsNullOrWhiteSpace(d.MarginLeft) || !string.IsNullOrWhiteSpace(d.MarginRight)) reasons.Add("margins");
        if (string.Equals(d.Orientation, "landscape", StringComparison.OrdinalIgnoreCase)
            || (d.LatexDocumentClassOptions ?? "").Contains("landscape", StringComparison.OrdinalIgnoreCase)) reasons.Add("landscape");
        if (d.Columns > 1) reasons.Add("columns");
        if (d.LineSpacing is { } ls && Math.Abs(ls - 1.0) > 0.001) reasons.Add("line spacing");   // 1.0 is single spacing: nothing to honour
        if (!string.IsNullOrWhiteSpace(d.ParagraphIndent)) reasons.Add("paragraph indent");
        // Typst numbers no heading; numbered headings are a LaTeX setting.
        if (d.HeadingNumbering is > 0) reasons.Add("heading numbering");
        if (d.PageNumbering is { } pn && !string.Equals(pn.Trim(), "arabic", StringComparison.OrdinalIgnoreCase) && pn.Trim().Length > 0) reasons.Add("page numbering");
        if (new[] { d.HeaderText, d.FooterText, d.HeaderLeft, d.HeaderCenter, d.HeaderRight, d.FooterLeft, d.FooterCenter, d.FooterRight }
            .Any(v => !string.IsNullOrWhiteSpace(v))) reasons.Add("header or footer");
        // TikZ setup alone (\usetikzlibrary, \tikzset …) is not page setup: Typst places the
        // figures as the SVGs the server drew with it, so it does not need LaTeX for the document.
        if (!Lilia.Core.Blocks.TikzFigure.IsSetupOnly(d.CustomPreamble)) reasons.Add("custom preamble");
        var cls = d.LatexDocumentClass?.Trim();
        if (!string.IsNullOrEmpty(cls) && !string.Equals(cls, "article", StringComparison.OrdinalIgnoreCase)) reasons.Add($"document class {cls}");
        if (!string.IsNullOrWhiteSpace(d.LatexDocumentClassOptions)) reasons.Add("class options");
        // A document theme is a LaTeX package (lilia-theme.sty); Typst has no twin yet.
        if (Lilia.Engines.Themes.DocumentLook.IsThemed(d.Look, d.LatexDocumentClass)) reasons.Add("theme");
        return reasons.Count == 0 ? null : string.Join(", ", reasons.Distinct());
    }

}

/// <summary>
/// Whether a document's content can only be printed faithfully by LaTeX. The Typst path reads
/// Markdown-ish marks and plain maths; it printed <c>__under__</c>, <c>==hi==</c> and <c>\SI{3.0}{\metre}</c>
/// as text, dropped the caret in <c>\(e^{i\pi}\)</c>, left <c>\[ x^2 \]</c> raw and set
/// <c>$5 … $10</c> as maths (run 2, MCP-80) — with no error, so nothing fell back. The LaTeX path is
/// the one check_document verifies. Plain prose keeps Typst's speed.
/// </summary>
public static class ContentRouting
{
    private static readonly System.Text.RegularExpressions.Regex LatexOnly = new(
        @"\\[A-Za-z]|\\[(\[]|__\S|==\S|~~\S|\^\S[^^\s]*\^|%%\S|\[%|@cite[\[{]|@ref[\[{]|~\(",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Null when Typst prints every block as LaTeX would, else why not.</summary>
    public static string? WhyLatex(IEnumerable<(string Type, System.Text.Json.JsonElement Content)> blocks)
    {
        foreach (var (type, content) in blocks)
        {
            // Equations are LaTeX maths, which Typst's maths doesn't read.
            if (string.Equals(type, "equation", StringComparison.OrdinalIgnoreCase)) return "equations";
            foreach (var text in Strings(content))
            {
                if (LatexOnly.IsMatch(text)) return $"LaTeX or Lilia marks in a {type} block";
                if (Lilia.Engines.CurrencyDollar.Protect(text, _ => "\u0001").Contains('\u0001')) return $"a price in a {type} block";
                if (text.Contains('$')) return $"maths in a {type} block";
            }
        }
        return null;
    }

    private static IEnumerable<string> Strings(System.Text.Json.JsonElement e) => e.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => [e.GetString() ?? ""],
        System.Text.Json.JsonValueKind.Array => e.EnumerateArray().SelectMany(Strings),
        System.Text.Json.JsonValueKind.Object => e.EnumerateObject().Where(p => p.Name is not ("src" or "url" or "code" or "language")).SelectMany(p => Strings(p.Value)),
        _ => [],
    };
}
