using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// A table of contents block's own title. Every export printed the class's or language's stock heading ("Contents")
/// whatever the block said, while the editor shows the block's title (the document sweep, 16 Nov 2026: about 70
/// documents in chunks 2 and 3). The stock titles ("Table of Contents", the editor's default, and "Contents") and an
/// empty one mean "the default", so a French document still says "Table des matières" and a publisher's class keeps
/// its own heading; anything else is the author's and is printed.
/// </summary>
public static class TocTitle
{
    private static readonly string[] Stock = ["table of contents", "contents"];

    /// <summary>The block's title when the author set one, or null for the export's own default.</summary>
    public static string? Custom(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("title", out var t) || t.ValueKind != JsonValueKind.String)
            return null;
        var title = (t.GetString() ?? "").Trim();
        return title.Length == 0 || Stock.Contains(title.ToLowerInvariant()) ? null : title;
    }
}
