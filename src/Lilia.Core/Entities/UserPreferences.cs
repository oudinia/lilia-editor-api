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
    public JsonDocument KeyboardShortcuts { get; set; } = JsonDocument.Parse("{}");
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User User { get; set; } = null!;
}
