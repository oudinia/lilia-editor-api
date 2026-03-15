using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IDocumentService
{
    /// <summary>
    /// Get paginated list of documents for a user
    /// </summary>
    Task<PaginatedResult<DocumentListDto>> GetDocumentsPaginatedAsync(
        string userId,
        int page = 1,
        int pageSize = 20,
        string? search = null,
        Guid? labelId = null,
        string sortBy = "updatedAt",
        string sortDir = "desc");

    /// <summary>
    /// Get all documents for a user (legacy, for backwards compatibility)
    /// </summary>
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

    /// <summary>
    /// Get paginated list of soft-deleted documents for a user
    /// </summary>
    Task<PaginatedResult<TrashDocumentDto>> GetTrashDocumentsPaginatedAsync(string userId, int page = 1, int pageSize = 20);

    /// <summary>
    /// Restore a soft-deleted document
    /// </summary>
    Task<bool> RestoreDocumentAsync(Guid id, string userId);

    /// <summary>
    /// Permanently delete a soft-deleted document
    /// </summary>
    Task<bool> PermanentDeleteDocumentAsync(Guid id, string userId);

    /// <summary>
    /// Purge all documents that have been in trash longer than the retention period
    /// </summary>
    Task<int> PurgeExpiredDocumentsAsync(int retentionDays = 30);

    /// <summary>
    /// Clone starter/sample documents to a new user for onboarding
    /// </summary>
    Task<int> CloneStarterDocumentsAsync(string userId);
}
