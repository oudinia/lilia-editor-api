using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// A text whose first heading level is not 1 (Markdown that starts at <c>##</c>, a PDF whose headings read as
/// subsections) printed "0.1": a subsection with no section before it (the launch bot's #79, 12 Nov 2026). For
/// Markdown and PDF imports the shallowest heading becomes a section and the rest move up with it, keeping their
/// order. A LaTeX import keeps the author's own structure.
/// </summary>
public static class HeadingLevels
{
    public static List<ImportElement> Promote(List<ImportElement> elements)
    {
        var headings = elements.OfType<ImportHeading>().Where(h => h.Level >= 1).ToList();
        if (headings.Count == 0) return elements;
        var shift = headings.Min(h => h.Level) - 1;
        if (shift <= 0) return elements;
        foreach (var h in headings) h.Level -= shift;
        return elements;
    }
}
