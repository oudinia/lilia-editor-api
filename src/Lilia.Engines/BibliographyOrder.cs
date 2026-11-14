using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// The order BibTeX prints a document's references in, for the check's own list (MCP-119, retested
/// 14 Nov): the check listed them as written, the PDF's plain style sorts them by author, so the same
/// citation was [1] in get_lilia_latex and [2] in the PDF. The export always lets BibTeX order them
/// (plain or plainnat by default, or the publisher's style); every style it uses sorts by author,
/// year and title except the unsrt family and IEEEtran, which keep citation order.
/// </summary>
public static class BibliographyOrder
{
    private static readonly HashSet<string> CitationOrder = new(StringComparer.OrdinalIgnoreCase)
        { "unsrt", "unsrtnat", "IEEEtran", "ieeetr", "IEEEtranN", "IEEEtranS" };

    /// <summary>Whether the style lists references in citation order (else sorted).</summary>
    public static bool KeepsCitationOrder(string? style) => style is not null && CitationOrder.Contains(style);

    /// <summary>The entries as BibTeX's sorting styles print them; a stable sort, so ties keep their order.</summary>
    public static List<BibliographyEntry> Sorted(IEnumerable<BibliographyEntry> entries) =>
        entries.Select((e, i) => (e, i)).OrderBy(x => SortKey(x.e), StringComparer.Ordinal).ThenBy(x => x.i).Select(x => x.e).ToList();

    /// <summary>BibTeX plain's sort key: the authors (last names, then first), then the year, then the title.</summary>
    public static string SortKey(BibliographyEntry entry)
    {
        string Field(string name)
        {
            try
            {
                return entry.Data?.RootElement is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v)
                    && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            }
            catch (ObjectDisposedException) { return ""; }
        }
        var people = Field("author");
        if (people.Length == 0) people = Field("editor");
        var names = people.Length == 0 ? entry.CiteKey
            : string.Join("   ", Regex.Split(people, @"\s+and\s+").Select(Name));
        return Purify(names) + "    " + Purify(Field("year")) + "    " + Purify(Regex.Replace(Field("title"), @"^(?:A|An|The)\s+", ""));
    }

    // "Last, First" or "First Last" → "Last  First".
    private static string Name(string name)
    {
        var comma = name.IndexOf(',');
        if (comma >= 0) return name[..comma].Trim() + "  " + name[(comma + 1)..].Trim();
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? name.Trim() : parts[^1] + "  " + string.Join(" ", parts[..^1]);
    }

    // As BibTeX's purify$ and lower case: accents and braces gone, letters, digits and spaces kept.
    private static string Purify(string s)
    {
        s = Regex.Replace(s, @"\\[A-Za-z]+\s*|\\.", "");
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
