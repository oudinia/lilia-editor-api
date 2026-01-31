using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IVersionService
{
    Task<List<VersionListDto>> GetVersionsAsync(Guid documentId);
    Task<VersionDto?> GetVersionAsync(Guid documentId, Guid versionId);
    Task<VersionDto> CreateVersionAsync(Guid documentId, string userId, CreateVersionDto dto);
    Task<DocumentDto?> RestoreVersionAsync(Guid documentId, Guid versionId, string userId);
    Task<bool> DeleteVersionAsync(Guid documentId, Guid versionId, string userId);
}
