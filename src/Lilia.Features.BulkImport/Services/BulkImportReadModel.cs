using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>
/// How batches and files stand, counted per page: a file's <c>pagesDone</c> is the pages of its placed chunks plus the
/// pages Mathpix has read so far of its chunks in flight (a chunk read and waiting for its place counts whole, as does a
/// cache hit); <c>pagesFailed</c> the pages of its failed chunks.
/// </summary>
public static class BulkImportReadModel
{
    private sealed record ChunkRow(Guid FileId, string Status, int FirstPage, int LastPage, int PagesCompleted);

    public static async Task<IReadOnlyList<BulkImportFileDto>> FilesAsync(LiliaDbContext db, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        if (fileIds.Count == 0) return [];
        var files = await db.BulkImportFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.OriginalName)
            .ToListAsync(ct);
        var chunks = await db.BulkImportChunks.AsNoTracking()
            .Where(c => fileIds.Contains(c.FileId))
            .Select(c => new ChunkRow(c.FileId, c.Status, c.FirstPage, c.LastPage, c.PagesCompleted))
            .ToListAsync(ct);
        var byFile = chunks.ToLookup(c => c.FileId);
        return files.Select(f => ToDto(f, byFile[f.Id].ToList())).ToList();
    }

    public static async Task<BulkImportFileDto?> FileAsync(LiliaDbContext db, Guid fileId, CancellationToken ct = default) =>
        (await FilesAsync(db, [fileId], ct)).FirstOrDefault();

    private static BulkImportFileDto ToDto(BulkImportFile f, List<ChunkRow> chunks)
    {
        int done = 0, failed = 0, pagesDone = 0, pagesFailed = 0;
        foreach (var c in chunks)
        {
            var pages = c.LastPage - c.FirstPage + 1;
            switch (c.Status)
            {
                case BulkImportStatuses.Done:
                    done++;
                    pagesDone += pages;
                    break;
                case BulkImportStatuses.Failed:
                    failed++;
                    pagesFailed += pages;
                    break;
                case BulkImportStatuses.Reading or BulkImportStatuses.Read:
                    pagesDone += Math.Clamp(c.PagesCompleted, 0, pages);
                    break;
            }
        }
        return new BulkImportFileDto(f.Id, f.OriginalName, f.Pages, pagesDone, pagesFailed, f.Status,
            new BulkImportChunkCountsDto(chunks.Count, done, failed), f.DocumentId, f.Error, f.Warnings);
    }

    /// <summary>A batch's status from its files': open (none), processing, done, cancelled, failed, or partial.</summary>
    public static string BatchStatus(IReadOnlyCollection<string> fileStatuses)
    {
        if (fileStatuses.Count == 0) return BulkImportStatuses.Open;
        if (fileStatuses.Any(s => Array.IndexOf(BulkImportStatuses.FileFinished, s) < 0)) return BulkImportStatuses.Processing;
        if (fileStatuses.All(s => s == BulkImportStatuses.Done)) return BulkImportStatuses.Done;
        if (fileStatuses.All(s => s == BulkImportStatuses.Cancelled)) return BulkImportStatuses.Cancelled;
        if (fileStatuses.All(s => s == BulkImportStatuses.Failed)) return BulkImportStatuses.Failed;
        return BulkImportStatuses.Partial;
    }

    public static async Task<BulkImportBatchDto?> BatchAsync(LiliaDbContext db, string userId, Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.BulkImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId, ct);
        if (batch is null) return null;
        var fileIds = await db.BulkImportFiles.AsNoTracking().Where(f => f.BatchId == batchId).Select(f => f.Id).ToListAsync(ct);
        var files = await FilesAsync(db, fileIds, ct);
        return new BulkImportBatchDto(batch.Id, BatchStatus(files.Select(f => f.Status).ToList()), batch.CreatedAt,
            files.Sum(f => f.Pages), files.Sum(f => f.PagesDone), files.Sum(f => f.PagesFailed), files);
    }

    public static async Task<IReadOnlyList<BulkImportBatchSummaryDto>> BatchesAsync(LiliaDbContext db, string userId, int limit = 50, CancellationToken ct = default)
    {
        var batches = await db.BulkImportBatches.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        var batchIds = batches.Select(b => b.Id).ToList();
        var fileRefs = await db.BulkImportFiles.AsNoTracking()
            .Where(f => batchIds.Contains(f.BatchId))
            .Select(f => new { f.Id, f.BatchId })
            .ToListAsync(ct);
        var files = (await FilesAsync(db, fileRefs.Select(f => f.Id).ToList(), ct)).ToDictionary(f => f.FileId);
        var byBatch = fileRefs.ToLookup(f => f.BatchId, f => files[f.Id]);
        return batches.Select(b =>
        {
            var fs = byBatch[b.Id].ToList();
            return new BulkImportBatchSummaryDto(b.Id, BatchStatus(fs.Select(f => f.Status).ToList()), b.CreatedAt,
                fs.Sum(f => f.Pages), fs.Sum(f => f.PagesDone), fs.Count,
                fs.Count(f => f.Status == BulkImportStatuses.Done),
                fs.Count(f => f.Status is BulkImportStatuses.Failed or BulkImportStatuses.Partial));
        }).ToList();
    }
}
