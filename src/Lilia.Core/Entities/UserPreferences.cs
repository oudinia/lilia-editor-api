using System.Text.Json;

namespace Lilia.Core.Entities;

public class UserPreferences
{
    public string UserId { get; set; } = string.Empty;
    public string Theme { get; set; } = "system";
    public string? DefaultFontFamily { get; set; }
    public int? DefaultFontSize { get; set; }
    public string? DefaultPaperSize { get; set; }
    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveInterval { get; set; } = 2000;
    /// <summary>
    /// The user's keyboard shortcuts — and, under <see cref="PinnedToolsKey"/>,
    /// the Lilia menu's pinned tools.
    /// </summary>
    public JsonDocument KeyboardShortcuts { get; set; } = JsonDocument.Parse("{}");

    /// <summary>
    /// Reserved key inside <see cref="KeyboardShortcuts"/> holding the pinned
    /// tools, a JSON array of tool keys in the user's order.
    /// </summary>
    /// <remarks>
    /// Stored there rather than in a column of its own to avoid a migration:
    /// this jsonb is already the user's own bag of UI customisation, and a
    /// short list of strings sits in it naturally. The API never shows the key
    /// as a shortcut — it is lifted out into <c>pinnedTools</c> on read, and a
    /// shortcuts write keeps it.
    /// </remarks>
    public const string PinnedToolsKey = "_pinnedTools";
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultExportFormat { get; set; } = "PDF";
    public JsonDocument? ExportOptions { get; set; }
    public bool SidebarCollapsed { get; set; }
    public bool PreviewEnabled { get; set; } = true;
    /// <summary>How the editor's "saved" moment looks: "pro" (default) or "fun". See <see cref="Personalities"/>.</summary>
    public string Personality { get; set; } = Personalities.Pro;
    /// <summary>
    /// How Ask Lilia explains: "beginner", "intermediate" or "advanced" (see <see cref="AskLevels"/>).
    /// Null until the author answers the one-time question; Ask Lilia treats null as beginner.
    /// </summary>
    public string? AskLevel { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User User { get; set; } = null!;
}

/// <summary>
/// The values of <see cref="UserPreferences.AskLevel"/> (the user, 12 Oct: ask once, save it on the
/// account, skipping means beginner). The help pages' Simple · Standard · For LaTeX users.
/// </summary>
public static class AskLevels
{
    public const string Beginner = "beginner";
    public const string Intermediate = "intermediate";
    public const string Advanced = "advanced";
    public const int MaxLength = 16;

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Beginner or "new" => Beginner,
        Intermediate or "some" => Intermediate,
        Advanced or "pro" => Advanced,
        _ => null,
    };
}

/// <summary>
/// The values of <see cref="UserPreferences.Personality"/>. Stored lowercase.
/// </summary>
public static class Personalities
{
    public const string Pro = "pro";
    public const string Fun = "fun";
    public const int MaxLength = 16;

    /// <summary>
    /// Case-insensitive match against the known personalities; returns the
    /// stored (lowercase) form, or null for anything else.
    /// </summary>
    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Pro => Pro,
        Fun => Fun,
        _ => null,
    };
}
