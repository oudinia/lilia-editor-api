using System.Text.Json;

namespace Lilia.Api.Services;

/// <summary>A document's stored look (the theme settings JSON), read; null when absent or unreadable.
/// Moved from DocumentService (2 Nov 2026) so the Library slice can use it from its own project.</summary>
public static class LookJson
{
    public static JsonElement? Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            using var doc = JsonDocument.Parse(stored);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
