using System.Text.Json;
using Lilia.Core.Blocks;

namespace Lilia.Engines;

/// <summary>
/// The numbers a compile gave a document's labels, in the form they are kept
/// between compiles.
///
/// <para>Only the author's labels. The <c>blk-…</c> labels the renderer emits
/// for the page map are ours, are one per block, and no reference ever points
/// at them — keeping them would multiply the stored size for nothing.</para>
/// </summary>
public static class LabelNumbers
{
    private sealed record Entry(string? N, int? P);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The author's labels from an .aux, as stored JSON — or null when the
    /// compile produced none worth keeping, so a failed compile never
    /// overwrites the last good numbers with nothing.
    /// </summary>
    public static string? FromAux(string? auxContent)
    {
        var map = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var label in AuxPageMap.ReadAll(auxContent))
        {
            if (label.Key.StartsWith(AuxPageMap.LabelPrefix, StringComparison.Ordinal)) continue;
            // Last write wins, as everywhere the .aux is read.
            map[label.Key] = new Entry(label.Number, label.Page);
        }
        return map.Count == 0 ? null : JsonSerializer.Serialize(map, Json);
    }

    /// <summary>
    /// Stored JSON back to numbers. Malformed or empty input reads as "no
    /// numbers", never as an error: they are a convenience layered over a
    /// correct index, and a bad row must not take the index down.
    /// </summary>
    public static IReadOnlyDictionary<string, AuxLabel> Parse(string? stored)
    {
        var result = new Dictionary<string, AuxLabel>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(stored)) return result;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(stored, Json);
            if (map is null) return result;
            foreach (var (key, e) in map) result[key] = new AuxLabel(key, e.N, e.P);
        }
        catch (JsonException)
        {
            // Treated as absent — see summary.
        }
        return result;
    }
}
