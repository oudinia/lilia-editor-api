using System.Text.Json;

namespace Lilia.Core.DTOs;

public record UserPreferencesDto(
    string UserId,
    string Theme,
    string? DefaultFontFamily,
    int? DefaultFontSize,
    string? DefaultPaperSize,
    bool AutoSaveEnabled,
    int AutoSaveInterval,
    string Personality,
    JsonElement KeyboardShortcuts,
    DateTime UpdatedAt
);

public record UpdatePreferencesDto(
    string? Theme,
    string? DefaultFontFamily,
    int? DefaultFontSize,
    string? DefaultPaperSize,
    bool? AutoSaveEnabled,
    int? AutoSaveInterval,
    string? Personality = null
);

public record UpdateKeyboardShortcutsDto(
    JsonElement Shortcuts
);
