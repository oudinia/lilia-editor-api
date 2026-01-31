using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class PreferencesService : IPreferencesService
{
    private readonly LiliaDbContext _context;

    public PreferencesService(LiliaDbContext context)
    {
        _context = context;
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
            await _context.SaveChangesAsync();
        }

        return MapToDto(preferences);
    }

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
