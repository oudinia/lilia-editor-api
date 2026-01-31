using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ILabelService
{
    Task<List<LabelDto>> GetLabelsAsync(string userId);
    Task<LabelDto?> GetLabelAsync(string userId, Guid labelId);
    Task<LabelDto> CreateLabelAsync(string userId, CreateLabelDto dto);
    Task<LabelDto?> UpdateLabelAsync(string userId, Guid labelId, UpdateLabelDto dto);
    Task<bool> DeleteLabelAsync(string userId, Guid labelId);
    Task<bool> AddLabelToDocumentAsync(Guid documentId, Guid labelId, string userId);
    Task<bool> RemoveLabelFromDocumentAsync(Guid documentId, Guid labelId, string userId);
}
