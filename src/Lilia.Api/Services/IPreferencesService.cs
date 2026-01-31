using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IPreferencesService
{
    Task<UserPreferencesDto> GetPreferencesAsync(string userId);
    Task<UserPreferencesDto> UpdatePreferencesAsync(string userId, UpdatePreferencesDto dto);
    Task<UserPreferencesDto> UpdateKeyboardShortcutsAsync(string userId, UpdateKeyboardShortcutsDto dto);
}
