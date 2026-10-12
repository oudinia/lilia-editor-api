using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// The references written inside a bibliography block as text — <c>@cite[knuth1984] Knuth, D. — The
/// TeXbook</c>, what LML's <c>@bibliography</c> and the AI skills write (MCP-04, 12 Oct). The PDF
/// printed only the document's bibliography table, which LML never fills, so these were silently
/// absent and every <c>\cite</c> of them printed "?".
/// </summary>
public static class BibliographyText
{
    public sealed record Entry(string Key, string Text);

    private static readonly Regex Item = new(
        @"@cite\[(?<key>[^\]\s]+)\]\s*(?<text>.*?)(?=@cite\[|\z)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static IReadOnlyList<Entry> Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<Entry>()
            : Item.Matches(text)
                .Select(m => new Entry(m.Groups["key"].Value.Trim(), Regex.Replace(m.Groups["text"].Value, @"\s+", " ").Trim()))
                .Where(e => e.Key.Length > 0)
                .ToList();
}
