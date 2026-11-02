using Lilia.Core.DTOs;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Library.Services;

public interface ITemplateService
{
    Task<List<TemplateListDto>> GetTemplatesAsync(string userId, string? category = null);
    Task<TemplateDto?> GetTemplateAsync(Guid templateId, string userId);
    Task<TemplateDto?> CreateTemplateAsync(string userId, CreateTemplateDto dto);
    Task<TemplateDto?> UpdateTemplateAsync(Guid templateId, string userId, UpdateTemplateDto dto);
    Task<bool> DeleteTemplateAsync(Guid templateId, string userId);
    Task<DocumentDto?> UseTemplateAsync(Guid templateId, string userId, UseTemplateDto dto);
    Task<List<TemplateCategoryDto>> GetCategoriesAsync();
}
