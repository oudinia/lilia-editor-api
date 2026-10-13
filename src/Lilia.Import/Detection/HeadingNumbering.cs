using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Detection;

/// <summary>
/// Whether an imported document numbered its headings, for the document's heading-numbering
/// setting (Document.HeadingNumbering, 13 Oct: the author's choice, and a DOCX import should
/// start from what the Word file did). A Word file numbers headings through a list on the
/// heading styles, on the paragraph, or as typed text ("1.2 Method").
/// </summary>
public static class HeadingNumbering
{
    private static readonly Regex TypedNumber = new(@"^(?:\d+(?:\.\d+)*\.?\s+|[IVXLC]+\.\s+|[A-Z]\.\s+)\S", RegexOptions.Compiled);

    public static bool IsNumbered(ParagraphAnalysis a) =>
        (a.HasNumberingProperties && a.NumberingId is not (null or 0))
        || a.StyleNumbered
        || TypedNumber.IsMatch(a.Text ?? "");

    /// <summary>
    /// Null when there are no headings; 0 when none was numbered; else the deepest level that was,
    /// counting only when most level-1 headings (or most headings, without level 1) were numbered.
    /// </summary>
    public static int? Detect(IEnumerable<ImportElement> elements)
    {
        var headings = elements.OfType<ImportHeading>().ToList();
        if (headings.Count == 0) return null;
        var numbered = headings.Where(h => h.SourceNumbered).ToList();
        if (numbered.Count == 0) return 0;
        var top = headings.Where(h => h.Level == headings.Min(x => x.Level)).ToList();
        if (top.Count(h => h.SourceNumbered) * 2 < top.Count) return 0;
        return Math.Clamp(numbered.Max(h => h.Level), 1, 5);
    }
}
