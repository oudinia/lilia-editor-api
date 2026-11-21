using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Core.Entities;
using Lilia.Import.Services;
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
    private sealed record ChunkRow(Guid FileId, string Status, int FirstPage, int LastPage, int PagesCompleted,
        bool FromCache, int? QueueWaitMs, int? MathpixMs, int? TexZipMs, int? PlaceMs, string? PrecisionPages);

    public static async Task<IReadOnlyList<BulkImportFileDto>> FilesAsync(LiliaDbContext db, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        if (fileIds.Count == 0) return [];
        var files = await db.BulkImportFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.OriginalName)
            .ToListAsync(ct);
        var chunks = await db.BulkImportChunks.AsNoTracking()
            .Where(c => fileIds.Contains(c.FileId))
            .Select(c => new ChunkRow(c.FileId, c.Status, c.FirstPage, c.LastPage, c.PagesCompleted,
                c.FromCache, c.QueueWaitMs, c.MathpixMs, c.TexZipMs, c.PlaceMs, c.PrecisionPages))
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
        var placedPages = chunks.Where(c => c.Status == BulkImportStatuses.Done).Sum(c => c.LastPage - c.FirstPage + 1);
        return new BulkImportFileDto(f.Id, f.OriginalName, f.Pages, pagesDone, pagesFailed, f.Status,
            new BulkImportChunkCountsDto(chunks.Count, done, failed), f.DocumentId, f.Error, f.Warnings,
            TimingsOf(f, chunks, placedPages), PrecisionOf(f, chunks));
    }

    /// <summary>The file's timings: its own, and its chunks' averaged (21 Nov 2026).</summary>
    private static BulkImportTimingsDto TimingsOf(BulkImportFile f, List<ChunkRow> chunks, int placedPages)
    {
        var read = chunks.Where(c => c.MathpixMs is not null && !c.FromCache).ToList();
        var readPages = read.Sum(c => c.LastPage - c.FirstPage + 1);
        int? PerChunk(Func<ChunkRow, int?> pick) =>
            chunks.Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList() is { Count: > 0 } values
                ? (int)Math.Round(values.Average()) : null;
        double? perMinute = f.TotalMs is > 0 && placedPages > 0 ? Math.Round(placedPages / (f.TotalMs.Value / 60000.0), 1) : null;
        return new BulkImportTimingsDto(
            f.UploadMs,
            f.SplitMs,
            f.FirstPageMs,
            f.TotalMs,
            readPages > 0 ? (int)Math.Round(read.Sum(c => (double)c.MathpixMs!.Value) / readPages) : null,
            PerChunk(c => c.FromCache ? null : c.TexZipMs),
            PerChunk(c => c.PlaceMs),
            PerChunk(c => c.QueueWaitMs),
            f.CompileMs,
            f.CompileOk,
            f.CompileEngine,
            perMinute,
            chunks.Count(c => c.FromCache));
    }

    /// <summary>The file's precision and its low pages, in page order; null until a page has a score.</summary>
    private static BulkImportPrecisionDto? PrecisionOf(BulkImportFile f, List<ChunkRow> chunks)
    {
        if (f.Precision is not { } score) return null;
        var low = chunks.SelectMany(c => MathpixPrecision.Parse(c.PrecisionPages)).Where(p => p.Low).Select(p => p.Page)
            .Distinct().Order().ToList();
        return new BulkImportPrecisionDto(score, low);
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
