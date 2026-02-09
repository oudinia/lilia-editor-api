using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ISnippetService
{
    Task<SnippetPageDto> GetSnippetsAsync(string userId, SnippetSearchDto search);
    Task<SnippetDto?> GetSnippetAsync(Guid id, string userId);
    Task<SnippetDto> CreateSnippetAsync(string userId, CreateSnippetDto dto);
    Task<SnippetDto?> UpdateSnippetAsync(Guid id, string userId, UpdateSnippetDto dto);
    Task<bool> DeleteSnippetAsync(Guid id, string userId);
    Task<SnippetDto?> ToggleFavoriteAsync(Guid id, string userId);
    Task<bool> IncrementUsageAsync(Guid id, string userId);
    Task<List<string>> GetCategoriesAsync();
}
