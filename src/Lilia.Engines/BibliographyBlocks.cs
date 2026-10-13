using System.Text.Json;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// What a document's bibliography blocks hold, for both emitters: BibTeX entries
/// (<c>@article{key, …}</c>, what the help teaches and every AI app writes — run 2, MCP-81: they
/// printed nothing, and every citation of them was [?]) and references written as text
/// (<c>@cite[key] Author — Title</c>, <see cref="BibliographyText"/>).
/// </summary>
public static class BibliographyBlocks
{
    /// <summary>The BibTeX entries written in the blocks, as bibliography entries.</summary>
    public static List<BibliographyEntry> BibTeX(IEnumerable<Block> blocks) =>
        Texts(blocks)
            .SelectMany(t => BibTexParser.Parse(t))
            .Select(e => new BibliographyEntry
            {
                CiteKey = e.CiteKey,
                EntryType = e.EntryType.ToLowerInvariant(),
                Data = JsonDocument.Parse(JsonSerializer.Serialize(
                    e.Fields.ToDictionary(f => f.Key.ToLowerInvariant(), f => f.Value))),
            })
            .GroupBy(e => e.CiteKey, StringComparer.Ordinal).Select(g => g.First())
            .ToList();

    /// <summary>The references written as text in the blocks.</summary>
    public static List<BibliographyText.Entry> Text(IEnumerable<Block> blocks) =>
        Texts(blocks).SelectMany(BibliographyText.Parse)
            .GroupBy(e => e.Key, StringComparer.Ordinal).Select(g => g.First())
            .ToList();

    private static IEnumerable<string> Texts(IEnumerable<Block> blocks) =>
        blocks.Where(b => string.Equals(b.Type, "bibliography", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Content?.RootElement is { ValueKind: JsonValueKind.Object } c
                && c.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))!;
}
