using Lilia.Api.Models.Documents;
using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public enum SetDocumentTeamStatus
{
    Ok = 0,
    DocumentNotFound = 1,
    NotOwner = 2,
    TeamNotAccessible = 3,
}

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
    /// <summary>
    /// Attach or detach a document to/from a team. Pass null to
    /// detach. Owner-only. The returned status distinguishes the
    /// three failure modes (doc not found / not owner / no access
    /// to the target team) so the controller can map them to the
    /// right HTTP code instead of one bare 404.
    /// </summary>
    Task<(DocumentDto? Document, SetDocumentTeamStatus Status)> SetDocumentTeamAsync(Guid id, string userId, Guid? teamId);

    /// <summary>
    /// Clone a publicly-shared document into the requester's
    /// library. Backs the "Make a copy" CTA on the public viewer
    /// chrome (spec §8). Differs from DuplicateDocumentAsync in
    /// that it gates on `IsPublic` instead of collaborator
    /// permissions — the doc being public IS the access. Returns
    /// null if the source isn't public.
    /// </summary>
    Task<DocumentDto?> CloneSharedDocumentAsync(string shareToken, string userId);
    Task<bool> DeleteDocumentAsync(Guid id, string userId);
    Task<DocumentDto?> DuplicateDocumentAsync(Guid id, string userId);
    Task<DocumentShareResultDto?> ShareDocumentAsync(Guid id, string userId, ShareDocumentDto dto);
    Task<bool> RevokeShareAsync(Guid id, string userId);
    Task<bool> HasAccessAsync(Guid documentId, string userId, string requiredPermission);

    /// <summary>
    /// Cheap projection: is the v2 "edit block as LaTeX" surface enabled for
    /// this document? Used by BlocksController to gate POST /from-latex.
    /// </summary>
    Task<bool> IsExperimentalLatexEditEnabledAsync(Guid documentId);

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
