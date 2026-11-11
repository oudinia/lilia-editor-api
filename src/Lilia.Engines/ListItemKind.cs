using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// The kind of a list nested in a list item, and the item's own text.
///
/// <para>A list block has one <c>ordered</c> for its top level. An item whose
/// sub-list is of the other kind carries <c>childrenOrdered</c>: a bullet list
/// under a numbered point is <c>{text, children, childrenOrdered: false}</c>.
/// Without it every level printed as the top level's kind (API #483, 11 Nov:
/// a pasted answer's bullet sub-points came out numbered).</para>
///
/// <para>Items are a string, or an object with <c>text</c> and optional
/// <c>children</c>; children are mixed the same way.</para>
/// </summary>
public static class ListItemKind
{
    /// <summary>Whether the list nested in <paramref name="item"/> is numbered; the parent's kind unless the item says.</summary>
    public static bool ChildrenOrdered(JsonElement item, bool parentOrdered) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("childrenOrdered", out var k)
        && k.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? k.GetBoolean()
            : parentOrdered;

    /// <summary>Whether the item says its sub-list's kind at all (a description list's sub-list keeps "description" otherwise).</summary>
    public static bool? ChildrenOrderedOrNull(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("childrenOrdered", out var k)
        && k.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? k.GetBoolean()
            : null;

    /// <summary>The item's text: the string itself, or the object's <c>text</c>.</summary>
    public static string Text(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.String => item.GetString() ?? "",
        JsonValueKind.Object when item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String => t.GetString() ?? "",
        _ => "",
    };

    /// <summary>The item's nested items, when it has any.</summary>
    public static bool TryChildren(JsonElement item, out JsonElement children)
    {
        children = default;
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("children", out var c)
            || c.ValueKind != JsonValueKind.Array
            || c.GetArrayLength() == 0) return false;
        children = c;
        return true;
    }
}
