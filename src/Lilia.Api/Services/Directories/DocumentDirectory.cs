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

    // Moved unchanged from TeamService.GetTeamDocumentsAsync (3 Nov 2026).
    public async Task<IReadOnlyList<Lilia.Core.DTOs.DocumentListDto>> TeamDocumentsAsync(Guid teamId, string teamName, CancellationToken ct = default)
    {
        var documents = await db.Documents
            .Include(d => d.Owner)
            .Include(d => d.DocumentLabels)
                .ThenInclude(dl => dl.Label)
            .Where(d => d.TeamId == teamId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        // Get block counts for these documents
        var documentIds = documents.Select(d => d.Id).ToList();
        var blockCounts = await db.Blocks
            .Where(b => documentIds.Contains(b.DocumentId))
            .GroupBy(b => b.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, ct);

        // Get section counts (heading blocks)
        var sectionCounts = await db.Blocks
            .Where(b => documentIds.Contains(b.DocumentId) && b.Type == "heading")
            .GroupBy(b => b.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, ct);

        return documents.Select(d => new Lilia.Core.DTOs.DocumentListDto(
            d.Id,
            d.Title,
            d.OwnerId,
            d.Owner?.Name,
            d.TeamId,
            teamName,
            d.CreatedAt,
            d.UpdatedAt,
            d.LastOpenedAt,
            blockCounts.GetValueOrDefault(d.Id, 0),
            sectionCounts.GetValueOrDefault(d.Id, 0),
            new List<Lilia.Core.DTOs.OutlineItemDto>(), // Team documents don't need outline for now
            d.DocumentLabels.Select(dl => new Lilia.Core.DTOs.LabelDto(
                dl.Label.Id,
                dl.Label.Name,
                dl.Label.Color,
                dl.Label.CreatedAt
            )).ToList(),
            Look: DocumentService.ReadLook(d.Look)
        )).ToList();
    }
}
