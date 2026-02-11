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
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultExportFormat { get; set; } = "PDF";
    public JsonDocument? ExportOptions { get; set; }
    public bool SidebarCollapsed { get; set; }
    public bool PreviewEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User User { get; set; } = null!;
}
