using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IDocumentService
{
    Task<List<DocumentListDto>> GetDocumentsAsync(string userId, string? search = null, Guid? labelId = null);
    Task<DocumentDto?> GetDocumentAsync(Guid id, string userId);
    Task<DocumentDto?> GetSharedDocumentAsync(string shareLink);
    Task<DocumentDto> CreateDocumentAsync(string userId, CreateDocumentDto dto);
    Task<DocumentDto?> UpdateDocumentAsync(Guid id, string userId, UpdateDocumentDto dto);
    Task<bool> DeleteDocumentAsync(Guid id, string userId);
    Task<DocumentDto?> DuplicateDocumentAsync(Guid id, string userId);
    Task<DocumentShareResultDto?> ShareDocumentAsync(Guid id, string userId, bool isPublic);
    Task<bool> RevokeShareAsync(Guid id, string userId);
    Task<bool> HasAccessAsync(Guid documentId, string userId, string requiredPermission);
}
