using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;

namespace Lilia.Api.Services;

public partial class PreferencesService : IPreferencesService
{
    public const int MaxPinnedTools = 12;

    [GeneratedRegex("^[a-z][a-z-]{0,31}$")]
    private static partial Regex ToolKeyPattern();

    /// <summary>Why a pinned-tools list is refused, or null when it is fine. Empty is fine.</summary>
    public static string? ValidatePinnedTools(IReadOnlyList<string?> tools)
    {
        if (tools.Count > MaxPinnedTools)
            return $"At most {MaxPinnedTools} tools can be pinned.";
        foreach (var tool in tools)
        {
            if (tool is null || !ToolKeyPattern().IsMatch(tool))
                return $"'{tool}' is not a tool key (lower-case letters and hyphens, starting with a letter, at most 32).";
        }
        if (tools.Distinct(StringComparer.Ordinal).Count() != tools.Count)
            return "A tool can be pinned only once.";
        return null;
    }

    private readonly LiliaDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<PreferencesService> _logger;

    public PreferencesService(
        LiliaDbContext context,
        IDistributedCache cache,
        ILogger<PreferencesService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<UserPreferencesDto> GetPreferencesAsync(string userId)
    {
        var preferences = await _context.UserPreferences.FindAsync(userId);

        if (preferences == null)
        {
            // Create default preferences
            preferences = new UserPreferences
            {
                UserId = userId,
                Theme = "system",
                AutoSaveEnabled = true,
                AutoSaveInterval = 2000,
                Personality = Personalities.Pro,
                KeyboardShortcuts = JsonDocument.Parse("{}"),
                UpdatedAt = DateTime.UtcNow
            };

            _context.UserPreferences.Add(preferences);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUserForeignKeyViolation(ex))
            {
                // Stale usersync cache: middleware skipped the user upsert
                // because of a recent cache hit, but the user row is gone
                // (manual cleanup / DB reset). Detach, invalidate the
                // cache so the next request re-syncs, and return defaults
                // without persisting. BG-040.
                _context.Entry(preferences).State = EntityState.Detached;
                await _cache.RemoveAsync($"usersync:{userId}");
                _logger.LogWarning(
                    "Preferences insert failed with FK violation for user {UserId}; usersync cache invalidated — next request will re-sync",
                    userId);
            }
        }

        return MapToDto(preferences);
    }

    // Match both the PG default name (prod: user_preferences_user_id_fkey)
    // and EF's generated name (test / fresh schema: FK_user_preferences_users_user_id).
    private static bool IsUserForeignKeyViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: "23503" } pg
           && (pg.ConstraintName?.Contains("user_preferences", StringComparison.Ordinal) ?? false);

    public async Task<UserPreferencesDto> UpdatePreferencesAsync(string userId, UpdatePreferencesDto dto)
    {
        var preferences = await _context.UserPreferences.FindAsync(userId);

        if (preferences == null)
        {
            preferences = new UserPreferences
            {
                UserId = userId,
                UpdatedAt = DateTime.UtcNow
            };
            _context.UserPreferences.Add(preferences);
        }

        if (dto.Theme != null) preferences.Theme = dto.Theme;
        if (dto.DefaultFontFamily != null) preferences.DefaultFontFamily = dto.DefaultFontFamily;
        if (dto.DefaultFontSize.HasValue) preferences.DefaultFontSize = dto.DefaultFontSize;
        if (dto.DefaultPaperSize != null) preferences.DefaultPaperSize = dto.DefaultPaperSize;
        if (dto.AutoSaveEnabled.HasValue) preferences.AutoSaveEnabled = dto.AutoSaveEnabled.Value;
        if (dto.AutoSaveInterval.HasValue) preferences.AutoSaveInterval = dto.AutoSaveInterval.Value;
        if (dto.Personality != null)
        {
            // The controller answers an unknown value with a 400 before it
            // gets here; this guard keeps any other caller from storing one.
            preferences.Personality = Personalities.Normalize(dto.Personality)
                ?? throw new ArgumentException($"Unknown personality '{dto.Personality}'.", nameof(dto));
        }
        if (dto.PinnedTools != null)
        {
            var error = ValidatePinnedTools(dto.PinnedTools);
            if (error != null) throw new ArgumentException(error, nameof(dto.PinnedTools));

            // Merged into the shortcuts bag; its other keys are left as they are.
            var bag = AsObject(preferences.KeyboardShortcuts);
            bag[UserPreferences.PinnedToolsKey] = new JsonArray(dto.PinnedTools.Select(t => (JsonNode?)t).ToArray());
            preferences.KeyboardShortcuts = JsonDocument.Parse(bag.ToJsonString());
        }

        preferences.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return MapToDto(preferences);
    }

    public async Task<UserPreferencesDto> UpdateKeyboardShortcutsAsync(string userId, UpdateKeyboardShortcutsDto dto)
    {
        var preferences = await _context.UserPreferences.FindAsync(userId);

        if (preferences == null)
        {
            preferences = new UserPreferences
            {
                UserId = userId,
                UpdatedAt = DateTime.UtcNow
            };
            _context.UserPreferences.Add(preferences);
        }

        preferences.KeyboardShortcuts = ReplaceShortcutsKeepingPins(preferences.KeyboardShortcuts, dto.Shortcuts);
        preferences.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return MapToDto(preferences);
    }

    /// <summary>
    /// The shortcuts write replaces the user's shortcuts — not the pinned tools
    /// stored beside them (see <see cref="UserPreferences.PinnedToolsKey"/>).
    /// The reserved key is never taken from the client here.
    /// </summary>
    private static JsonDocument ReplaceShortcutsKeepingPins(JsonDocument? stored, JsonElement incoming)
    {
        var pins = stored?.RootElement is { ValueKind: JsonValueKind.Object } root
                   && root.TryGetProperty(UserPreferences.PinnedToolsKey, out var p)
            ? JsonNode.Parse(p.GetRawText())
            : null;

        if (incoming.ValueKind != JsonValueKind.Object)
            return JsonDocument.Parse(incoming.GetRawText());

        var bag = (JsonObject)JsonNode.Parse(incoming.GetRawText())!;
        bag.Remove(UserPreferences.PinnedToolsKey);
        if (pins != null) bag[UserPreferences.PinnedToolsKey] = pins;
        return JsonDocument.Parse(bag.ToJsonString());
    }

    private static JsonObject AsObject(JsonDocument? doc) =>
        doc?.RootElement.ValueKind == JsonValueKind.Object
            ? (JsonObject)JsonNode.Parse(doc.RootElement.GetRawText())!
            : new JsonObject();

    private static UserPreferencesDto MapToDto(UserPreferences p)
    {
        // Lift the reserved key out: it is shown as pinnedTools, never as a shortcut.
        var shortcuts = p.KeyboardShortcuts.RootElement;
        string[]? pinned = null;
        if (shortcuts.ValueKind == JsonValueKind.Object
            && shortcuts.TryGetProperty(UserPreferences.PinnedToolsKey, out var stored))
        {
            pinned = stored.ValueKind == JsonValueKind.Array
                ? stored.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray()
                : null;
            var rest = AsObject(p.KeyboardShortcuts);
            rest.Remove(UserPreferences.PinnedToolsKey);
            shortcuts = JsonDocument.Parse(rest.ToJsonString()).RootElement;
        }

        return new UserPreferencesDto(
            p.UserId,
            p.Theme,
            p.DefaultFontFamily,
            p.DefaultFontSize,
            p.DefaultPaperSize,
            p.AutoSaveEnabled,
            p.AutoSaveInterval,
            p.Personality,
            shortcuts,
            p.UpdatedAt,
            pinned
        );
    }
}
