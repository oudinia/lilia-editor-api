using Lilia.Api.Directories;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services.Directories;

/// <summary>Documents' answer to <see cref="IDocumentDirectory"/>: read-only, untracked.</summary>
public sealed class DocumentDirectory(LiliaDbContext db) : IDocumentDirectory
{
    public Task<DocumentCard?> FindAsync(Guid documentId, CancellationToken ct = default) =>
        db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new DocumentCard(d.Id, d.Title, d.OwnerId))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OwnedDocumentCard>> ShareableOwnedByAsync(string ownerId, CancellationToken ct = default) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && d.DeletedAt == null && !d.IsTemplate && !d.IsPlayground)
            .Select(d => new OwnedDocumentCard(d.Id, d.Title, d.UpdatedAt, d.LastOpenedAt, d.IsPublic, d.ShareLink != null))
            .ToListAsync(ct);

    public Task<bool> HasBlockAsync(Guid documentId, Guid blockId, CancellationToken ct = default) =>
        db.Blocks.AnyAsync(b => b.Id == blockId && b.DocumentId == documentId, ct);
}
