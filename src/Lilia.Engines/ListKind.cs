using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// What kind of list a list block is — bullet, numbered or description — read the same way by every
/// emitter. The block has been written as <c>kind</c> (bullet | numbered | description),
/// <c>listType</c> (ordered | unordered, and what apps guess: numbered, bullet, description) and
/// <c>ordered: true</c>; the LaTeX emitter honoured only listType "ordered" and kind "description",
/// and Word only listType "ordered", so a numbered list came out as bullets and a description list
/// lost its descriptions (MCP-95, MCP-99, run 3).
/// </summary>
public static class ListKind
{
    public const string Bullet = "bullet";
    public const string Numbered = "numbered";
    public const string Description = "description";

    public static string Of(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return Bullet;
        foreach (var name in new[] { "kind", "listType", "type" })
            if (content.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Read(v.GetString()) is { } kind)
                return kind;
        return content.TryGetProperty("ordered", out var o) && o.ValueKind == JsonValueKind.True ? Numbered : Bullet;
    }

    private static string? Read(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "numbered" or "ordered" or "enumerate" or "number" or "numeric" => Numbered,
        "description" or "definition" or "descriptive" => Description,
        "bullet" or "bulleted" or "unordered" or "itemize" or "bullets" => Bullet,
        _ => null,
    };
}
