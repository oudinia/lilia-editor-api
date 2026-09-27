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
    DateTime UpdatedAt,
    // The Lilia menu's pinned tools, in the user's order. Null until first set;
    // empty means "pinned nothing". Never also present in KeyboardShortcuts.
    string[]? PinnedTools = null
);

public record UpdatePreferencesDto(
    string? Theme,
    string? DefaultFontFamily,
    int? DefaultFontSize,
    string? DefaultPaperSize,
    bool? AutoSaveEnabled,
    int? AutoSaveInterval,
    string? Personality = null,
    // When non-null, replaces the stored list: at most 12 distinct tool keys,
    // each ^[a-z][a-z-]{0,31}$. Empty is valid.
    string[]? PinnedTools = null
);

public record UpdateKeyboardShortcutsDto(
    JsonElement Shortcuts
);
