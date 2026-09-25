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
    /// The numbers a Typst preview gave its labels — the JSON
    /// <c>typst eval</c> returns for the probe the exporter writes,
    /// <c>[["tab:results","1",2], …]</c> — as stored JSON, in the same form as
    /// <see cref="FromAux"/>. Null when there is nothing usable, so a preview
    /// that numbered nothing never overwrites the last good numbers.
    /// </summary>
    public static string? FromTypst(string? evaluated)
    {
        if (string.IsNullOrWhiteSpace(evaluated)) return null;
        var map = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(evaluated);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 3) continue;
                var key = row[0].ValueKind == JsonValueKind.String ? row[0].GetString() : null;
                var number = row[1].ValueKind == JsonValueKind.String ? row[1].GetString() : null;
                int? page = row[2].ValueKind == JsonValueKind.Number && row[2].TryGetInt32(out var p) ? p : null;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(number)) continue;
                map[key] = new Entry(number, page);
            }
        }
        catch (JsonException)
        {
            return null;
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
