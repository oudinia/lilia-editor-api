namespace Lilia.Api.Directories;

/// <summary>
/// What another context may know about a document (2 Nov 2026, the bounded contexts, step 2): its title and
/// owner, asked by id, never by reading the documents table. Documents implements it; a trashed document is
/// not found, as the table's own filter has it.
/// </summary>
public interface IDocumentDirectory
{
    Task<DocumentCard?> FindAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>The documents an owner could share: not trashed, not a template, not the playground.</summary>
    Task<IReadOnlyList<OwnedDocumentCard>> ShareableOwnedByAsync(string ownerId, CancellationToken ct = default);

    /// <summary>Whether block <paramref name="blockId"/> is part of document <paramref name="documentId"/>.</summary>
    Task<bool> HasBlockAsync(Guid documentId, Guid blockId, CancellationToken ct = default);

    /// <summary>
    /// The documents put in team <paramref name="teamId"/>, newest first, as the team's page lists them
    /// (3 Nov 2026, Teams' bounded context, step 4: Teams checks the caller belongs, then asks this).
    /// </summary>
    Task<IReadOnlyList<Lilia.Core.DTOs.DocumentListDto>> TeamDocumentsAsync(Guid teamId, string teamName, CancellationToken ct = default);
}

public sealed record DocumentCard(Guid Id, string Title, string OwnerId);

public sealed record OwnedDocumentCard(Guid Id, string Title, DateTime UpdatedAt, DateTime? LastOpenedAt, bool IsPublic, bool HasShareLink);
