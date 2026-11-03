namespace Lilia.Api.Services;

/// <summary>
/// Whether a person may do something to a document (3 Nov 2026): its own small service, out of the 1,200-line
/// DocumentService, so what asks only this (Sharing's controllers, the RequireDocumentAccess filter) no
/// longer depends on everything else DocumentService does. <see cref="IDocumentService.HasAccessAsync"/>
/// answers the same, through this.
/// </summary>
public interface IDocumentAccess
{
    /// <summary>The owner may do anything; a share grants its role's permissions; a team document is open to the team for reading and writing. A trashed document grants nothing.</summary>
    Task<bool> CanAsync(Guid documentId, string userId, string permission, CancellationToken ct = default);
}
