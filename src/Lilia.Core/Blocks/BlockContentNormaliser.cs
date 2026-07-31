using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lilia.Core.Blocks;

/// <summary>
/// The single place block content is shaped before it is stored.
///
/// <para>There was no such place. <c>BlockService</c> wrote
/// <c>JsonDocument.Parse(dto.Content…)</c> straight through at four separate
/// sites, and nothing else in the codebase normalised content at all — so
/// "write the new shape" had no seam to hook and every writer would have had to
/// remember independently. This is that seam.</para>
///
/// <para><b>Deliberately conservative.</b> Content is schemaless by design and
/// most block types are free-form, so this only touches what it understands and
/// copies everything else through untouched. An unknown type, a non-object, or
/// malformed JSON is returned exactly as it arrived: normalisation is not a
/// validation layer, and refusing to store something because this file did not
/// recognise it would be a much worse failure than storing it unchanged.</para>
/// </summary>
public static class BlockContentNormaliser
{
    /// <summary>
    /// Shape content for storage. Returns a NEW <see cref="JsonDocument"/> the
    /// caller owns; the input is never mutated.
    /// </summary>
    public static JsonDocument Normalise(string? blockType, JsonElement content)
    {
        if (!IsEquation(blockType) || content.ValueKind != JsonValueKind.Object)
            return Clone(content);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content.GetRawText());
        }
        catch (JsonException)
        {
            return Clone(content);
        }

        if (node is not JsonObject obj) return Clone(content);

        var source = EquationContent.ReadSource(content);

        // Nothing to anchor to — an equation block with no source at all is
        // legitimate (a freshly inserted, still-empty one), and inventing
        // properties for it would just add noise to every new block.
        if (string.IsNullOrEmpty(source)) return Clone(content);

        obj["source"] = source;

        // `latex` is kept in step with `source` rather than dropped. Readers
        // outside this codebase — an export already on disk, a client built
        // against the old shape — still expect it, and the cost of keeping it
        // is one duplicated string per equation. Removing it is a later step,
        // once nothing reads it and existing rows have been backfilled.
        obj["latex"] = source;

        obj["notation"] ??= EquationContent.LatexNotation;

        // `ast` is deliberately NOT written. Absent and null are
        // indistinguishable in schemaless JSON, so writing nulls now reserves
        // nothing that adding the field later would not — see EquationContent.

        return JsonDocument.Parse(obj.ToJsonString());
    }

    /// <summary>Convenience for callers holding raw text rather than an element.</summary>
    public static JsonDocument NormaliseRaw(string? blockType, string rawJson)
    {
        try
        {
            using var parsed = JsonDocument.Parse(rawJson);
            return Normalise(blockType, parsed.RootElement);
        }
        catch (JsonException)
        {
            // Malformed JSON is the caller's problem to report, not ours to
            // swallow — parse again outside the try so it throws as it always did.
            return JsonDocument.Parse(rawJson);
        }
    }

    private static bool IsEquation(string? blockType) =>
        string.Equals(blockType, "equation", StringComparison.OrdinalIgnoreCase);

    private static JsonDocument Clone(JsonElement content) =>
        JsonDocument.Parse(content.GetRawText());
}
