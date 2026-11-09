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

    /// <summary>
    /// The entries written in the blocks under a key the stored entries don't hold, BibTeX first, then references
    /// written as text (their line as the formatted text). 9 Nov 2026: a draft wrote its references as
    /// <c>@cite[sleep1] …</c> lines; the PDF listed them, but the editor marked every citation missing and the
    /// bibliography panel showed none, because both read only the stored entries. Each gets an id fixed by its
    /// document and key, so a list keeps it in place between loads.
    /// </summary>
    public static List<BibliographyEntry> Unstored(Guid documentId, IEnumerable<Block> blocks, IEnumerable<string> storedKeys)
    {
        var known = storedKeys.ToHashSet(StringComparer.Ordinal);
        var list = BibTeX(blocks).Where(e => known.Add(e.CiteKey)).ToList();
        list.AddRange(Text(blocks).Where(e => known.Add(e.Key)).Select(e => new BibliographyEntry
        {
            CiteKey = e.Key,
            EntryType = BibliographyEntryTypes.Misc,
            Data = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["note"] = e.Text })),
            FormattedText = e.Text,
        }));
        foreach (var e in list)
        {
            e.DocumentId = documentId;
            e.Id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{documentId}/{e.CiteKey}")));
        }
        return list;
    }

    private static IEnumerable<string> Texts(IEnumerable<Block> blocks) =>
        blocks.Where(b => string.Equals(b.Type, "bibliography", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Content?.RootElement is { ValueKind: JsonValueKind.Object } c
                && c.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))!;
}
