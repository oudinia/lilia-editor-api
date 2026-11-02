using Lilia.Core.DTOs;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Library.Services;

public interface IFormulaService
{
    Task<FormulaPageDto> GetFormulasAsync(string userId, FormulaSearchDto search);
    Task<FormulaDto?> GetFormulaAsync(Guid id, string userId);
    Task<FormulaDto> CreateFormulaAsync(string userId, CreateFormulaDto dto);
    Task<FormulaDto?> UpdateFormulaAsync(Guid id, string userId, UpdateFormulaDto dto);
    Task<bool> DeleteFormulaAsync(Guid id, string userId);
    Task<FormulaDto?> ToggleFavoriteAsync(Guid id, string userId);
    Task<string?> IncrementUsageAsync(Guid id, string userId, string? label);
    Task<List<string>> GetCategoriesAsync();
    Task<List<string>> GetSubcategoriesAsync(string category);
    Task<List<string>> GetUserTagsAsync(string userId);
    Task<List<FormulaThemeCountDto>> GetThemeCountsAsync(string userId);
}
