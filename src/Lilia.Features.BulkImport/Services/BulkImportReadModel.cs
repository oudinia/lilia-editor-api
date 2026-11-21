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
            TimingsOf(f, chunks, placedPages), PrecisionOf(f, chunks),
            // From the split (21 Nov 2026, the launch bot's retest of #679): the preview has its placeholders at once, so
            // "Open" needn't wait for the first chunk to be staged. A finished file keeps it only if something was read.
            PreviewAvailable: chunks.Count > 0
                && (Array.IndexOf(BulkImportStatuses.FileFinished, f.Status) < 0 || done + failed > 0),
            PromotedAt: f.PromotedAt);
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
            chunks.Count(c => c.FromCache),
            f.PromoteMs);
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

    /// <summary>
    /// The file's read-only preview (21 Nov 2026): its staged blocks in page order, a placeholder for each chunk not staged
    /// yet. Null when the file isn't this author's (in this batch).
    /// </summary>
    public static async Task<BulkImportPreviewDto?> PreviewAsync(LiliaDbContext db, string userId, Guid batchId, Guid fileId, CancellationToken ct = default)
    {
        if (!await db.BulkImportFiles.AsNoTracking().AnyAsync(f => f.Id == fileId && f.BatchId == batchId && f.UserId == userId, ct)) return null;
        if (await FileAsync(db, fileId, ct) is not { } file) return null;
        var chunks = await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == fileId).OrderBy(c => c.Index)
            .Select(c => new { c.Index, c.Status, c.FirstPage, c.LastPage }).ToListAsync(ct);
        var staged = (await db.BulkImportStagedBlocks.AsNoTracking().Where(b => b.FileId == fileId)
                .OrderBy(b => b.ChunkIndex).ThenBy(b => b.Order).ToListAsync(ct))
            .ToLookup(b => b.ChunkIndex);
        var blocks = new List<BulkImportPreviewBlockDto>();
        foreach (var c in chunks)
        {
            if (staged.Contains(c.Index))
            {
                blocks.AddRange(staged[c.Index].Select(b => new BulkImportPreviewBlockDto(b.Id, b.Type, b.Content.RootElement.Clone(), c.Index)));
                continue;
            }
            // Done with nothing staged: placed in the document (promoted, or a file split before staging), or staged with no
            // block at all. Nothing to wait for.
            if (c.Status == BulkImportStatuses.Done) continue;
            var state = c.Status switch
            {
                BulkImportStatuses.Queued => "queued",
                BulkImportStatuses.Failed or BulkImportStatuses.Cancelled => "failed",
                _ => "reading",
            };
            blocks.Add(new BulkImportPreviewBlockDto(null, "placeholder",
                System.Text.Json.JsonSerializer.SerializeToElement(new { firstPage = c.FirstPage, lastPage = c.LastPage, state }), c.Index));
        }
        return new BulkImportPreviewDto(file.FileId, file.Name, file.Pages, file.PagesDone, file.Status, file.DocumentId, blocks);
    }

    /// <summary>
    /// The file's warnings, each with its kind, a plain sentence and where it is (21 Nov 2026, the launch bot's retest: "It
    /// never names the warning or the block"). Read from each chunk's import record (<see cref="BulkImportChunk.SessionId"/>:
    /// its review session's diagnostics, the ones the file's count sums): the chunk gives the pages, the diagnostic's block
    /// (<see cref="ImportDiagnostic.BlockId"/>, the review row's) gives the block, whose row id is the block's id in the
    /// staging and, at promotion, in the document. Null when the file isn't this author's (in this batch).
    /// </summary>
    public static async Task<BulkImportWarningListDto?> WarningsAsync(LiliaDbContext db, string userId, Guid batchId, Guid fileId, CancellationToken ct = default)
    {
        var file = await db.BulkImportFiles.AsNoTracking()
            .Where(f => f.Id == fileId && f.BatchId == batchId && f.UserId == userId)
            .Select(f => new { f.DocumentId, f.Warnings })
            .FirstOrDefaultAsync(ct);
        if (file is null) return null;

        var chunks = (await db.BulkImportChunks.AsNoTracking()
                .Where(c => c.FileId == fileId && c.SessionId != null)
                .Select(c => new { c.Index, c.FirstPage, c.LastPage, SessionId = c.SessionId!.Value })
                .ToListAsync(ct))
            .GroupBy(c => c.SessionId).ToDictionary(g => g.Key, g => g.First());
        var sessionIds = chunks.Keys.ToList();
        if (sessionIds.Count == 0) return new BulkImportWarningListDto(fileId, file.DocumentId, file.Warnings, false, []);

        // The same diagnostics the file's count sums (PdfImportPipeline): not dismissed, a warning or an error. Less a
        // tex.zip's inventory ("Found 6 image file(s): …") stored as a warning before 21 Nov 2026 (the launch bot's retest:
        // 6 of a file's 8 listed): new rows are info (LatexProjectResult.InformationNotices), these older ones left out.
        var diagnostics = await db.ImportDiagnostics.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId) && !d.Dismissed && (d.Severity == "warning" || d.Severity == "error"))
            .Select(d => new { d.SessionId, d.BlockId, d.Code, d.Category, d.Severity, d.Message, d.CreatedAt })
            .ToListAsync(ct);
        var inventories = diagnostics.RemoveAll(d => IsStoredInventory(d.Category, d.Code, d.Message));

        var keys = diagnostics.Where(d => d.BlockId != null).Select(d => d.BlockId!).Distinct().ToList();
        var rows = (await db.ImportBlockReviews.AsNoTracking()
                .Where(b => keys.Count > 0 && sessionIds.Contains(b.SessionId) && keys.Contains(b.BlockId))
                .Select(b => new { b.SessionId, b.BlockId, b.Id, Type = b.CurrentType ?? b.OriginalType, b.SortOrder })
                .ToListAsync(ct))
            .GroupBy(b => (b.SessionId, b.BlockId)).Select(g => g.First()).ToList();
        var rowOf = rows.ToDictionary(b => (b.SessionId, b.BlockId));

        // Where each block is in the document now: present (not deleted since), and its place, from 1.
        var numberOf = new Dictionary<Guid, int>();
        if (file.DocumentId is { } documentId && rows.Count > 0)
        {
            var ids = rows.Select(b => b.Id).ToList();
            var present = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId && ids.Contains(b.Id))
                .Select(b => new { b.Id, b.SortOrder }).ToListAsync(ct);
            if (present.Count > 0)
            {
                var orders = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId)
                    .OrderBy(b => b.SortOrder).Select(b => b.SortOrder).ToListAsync(ct);
                foreach (var p in present) numberOf[p.Id] = LowerBound(orders, p.SortOrder) + 1;
            }
        }

        var all = diagnostics.Select(d =>
            {
                var chunk = chunks[d.SessionId];
                var row = d.BlockId is { } key && rowOf.TryGetValue((d.SessionId, key), out var r) ? r : null;
                var (message, page) = PlainWarning(d.Message);
                var inDocument = row is not null && numberOf.ContainsKey(row.Id);
                return (Chunk: chunk.Index, Order: row?.SortOrder ?? -1, d.CreatedAt, Dto: new BulkImportWarningDto(
                    string.IsNullOrWhiteSpace(d.Code) ? d.Category : d.Code,
                    d.Severity,
                    message,
                    chunk.Index,
                    chunk.FirstPage,
                    chunk.LastPage,
                    page,
                    row?.Id,
                    row?.Type,
                    inDocument,
                    inDocument ? numberOf[row!.Id] : null));
            })
            .OrderBy(w => w.Chunk).ThenBy(w => w.Order).ThenBy(w => w.CreatedAt)
            .Select(w => w.Dto)
            .ToList();
        var listed = all.Take(BulkImportWarningListDto.Limit).ToList();
        // A file counted before 21 Nov 2026 has its inventories in its stored count; the list's total leaves them out.
        var total = Math.Max(all.Count, file.Warnings - inventories);
        return new BulkImportWarningListDto(fileId, file.DocumentId, total, all.Count > listed.Count, listed);
    }

    /// <summary>
    /// An extractor inventory stored as a warning before 21 Nov 2026: category <c>auto_shimmed</c>, no code, and the message
    /// of an inventory ("Found N image/bibliography/code listing file(s)…", "N other file(s) preserved as assets…").
    /// </summary>
    internal static bool IsStoredInventory(string category, string? code, string message) =>
        category == "auto_shimmed" && string.IsNullOrEmpty(code)
        && (message.StartsWith(LatexProjectResult.InventoryPrefix, StringComparison.Ordinal)
            || message.Contains(LatexProjectResult.OtherFilesKept, StringComparison.Ordinal));

    /// <summary>The first index whose value is at least <paramref name="value"/> in a sorted list.</summary>
    private static int LowerBound(List<int> sorted, int value)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// A diagnostic's message without the import's own "Block N (type): " (N counts within the chunk, which misled the
    /// tester) or "Page N: " (returned as the page), starting with a capital and ending with a full stop.
    /// </summary>
    internal static (string Message, int? Page) PlainWarning(string message)
    {
        var text = (message ?? "").Trim();
        int? page = null;
        if (System.Text.RegularExpressions.Regex.Match(text, @"^Block \d+ \([^)]*\):\s*") is { Success: true } block)
            text = text[block.Length..];
        else if (System.Text.RegularExpressions.Regex.Match(text, @"^Page (\d+):\s*") is { Success: true } p)
        {
            page = int.Parse(p.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            text = text[p.Length..];
        }
        if (text.Length == 0) return (message ?? "", page);
        text = char.ToUpperInvariant(text[0]) + text[1..];
        if (!".!?".Contains(text[^1])) text += ".";
        return (text, page);
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
