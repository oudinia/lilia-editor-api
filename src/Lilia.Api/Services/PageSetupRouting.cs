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
        if (d.LineSpacing is not null) reasons.Add("line spacing");
        if (!string.IsNullOrWhiteSpace(d.ParagraphIndent)) reasons.Add("paragraph indent");
        if (d.PageNumbering is { } pn && !string.Equals(pn.Trim(), "arabic", StringComparison.OrdinalIgnoreCase) && pn.Trim().Length > 0) reasons.Add("page numbering");
        if (new[] { d.HeaderText, d.FooterText, d.HeaderLeft, d.HeaderCenter, d.HeaderRight, d.FooterLeft, d.FooterCenter, d.FooterRight }
            .Any(v => !string.IsNullOrWhiteSpace(v))) reasons.Add("header or footer");
        if (!string.IsNullOrWhiteSpace(d.CustomPreamble)) reasons.Add("custom preamble");
        var cls = d.LatexDocumentClass?.Trim();
        if (!string.IsNullOrEmpty(cls) && !string.Equals(cls, "article", StringComparison.OrdinalIgnoreCase)) reasons.Add($"document class {cls}");
        if (!string.IsNullOrWhiteSpace(d.LatexDocumentClassOptions)) reasons.Add("class options");
        return reasons.Count == 0 ? null : string.Join(", ", reasons.Distinct());
    }
}
