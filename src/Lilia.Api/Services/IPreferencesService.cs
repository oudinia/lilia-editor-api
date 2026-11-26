using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IPreferencesService
{
    Task<UserPreferencesDto> GetPreferencesAsync(string userId);
    Task<UserPreferencesDto> UpdatePreferencesAsync(string userId, UpdatePreferencesDto dto);
    Task<UserPreferencesDto> UpdateKeyboardShortcutsAsync(string userId, UpdateKeyboardShortcutsDto dto);
    /// <summary>Whether the person turned on automatic AI document summaries (opt-in, 26 Nov 2026). False without a row.</summary>
    Task<bool> AutoDocumentSummariesAsync(string userId, CancellationToken ct = default);
}
