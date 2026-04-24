using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;

namespace Lilia.Api.Services;

public class PreferencesService : IPreferencesService
{
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

        preferences.KeyboardShortcuts = JsonDocument.Parse(dto.Shortcuts.GetRawText());
        preferences.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return MapToDto(preferences);
    }

    private static UserPreferencesDto MapToDto(UserPreferences p)
    {
        return new UserPreferencesDto(
            p.UserId,
            p.Theme,
            p.DefaultFontFamily,
            p.DefaultFontSize,
            p.DefaultPaperSize,
            p.AutoSaveEnabled,
            p.AutoSaveInterval,
            p.KeyboardShortcuts.RootElement,
            p.UpdatedAt
        );
    }
}
