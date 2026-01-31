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
    JsonElement KeyboardShortcuts,
    DateTime UpdatedAt
);

public record UpdatePreferencesDto(
    string? Theme,
    string? DefaultFontFamily,
    int? DefaultFontSize,
    string? DefaultPaperSize,
    bool? AutoSaveEnabled,
    int? AutoSaveInterval
);

public record UpdateKeyboardShortcutsDto(
    JsonElement Shortcuts
);
