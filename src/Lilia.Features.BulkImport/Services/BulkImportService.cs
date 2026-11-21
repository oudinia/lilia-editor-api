using System.Security.Cryptography;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Lilia.Core.Entities.BulkImportStatuses;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>Why a file was not added to a batch (the controller's answer).</summary>
public enum AddFileRefusal
{
    None,
    BatchNotFound,
    BatchClosed,
    TooManyFiles,
    TooLarge,
    NotPdf,
    PagesUnknown,
}

public sealed record AddFileResult(AddFileRefusal Refusal, BulkImportFileDto? File = null, string? Message = null);

/// <summary>
/// What the HTTP API does (21 Nov 2026): batches, files dropped one per request, the author's view of them, retry and
/// cancel. A file is checked whole before anything leaves the machine: a PDF by its bytes, its exact pages, and the plan
/// and credits for those pages plus the author's pages still waiting to be read.
/// </summary>
public sealed class BulkImportService(
    LiliaDbContext db,
    IStorageService storage,
    IPdfImportPipeline pipeline,
    IBulkImportQueue queue,
    BulkImportProcessor processor,
    BulkImportOptions options,
    ILogger<BulkImportService> logger)
{
    public async Task<Guid> CreateBatchAsync(string userId, CancellationToken ct = default)
    {
        var batch = new BulkImportBatch { Id = Guid.NewGuid(), UserId = userId, Status = Open, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.BulkImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch.Id;
    }

    /// <summary>
    /// Adds one PDF to the author's batch: kept in storage, its document made once it is split (queued). Throws a
    /// <see cref="QuotaExceededException"/> when the plan or credits don't cover its pages.
    /// </summary>
    public async Task<AddFileResult> AddFileAsync(string userId, Guid batchId, Stream content, string fileName, CancellationToken ct = default)
    {
        var batch = await db.BulkImportBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId, ct);
        if (batch is null) return new(AddFileRefusal.BatchNotFound);
        if (batch.Status == Cancelled) return new(AddFileRefusal.BatchClosed, Message: "This bulk import was cancelled: start a new one.");
        if (await db.BulkImportFiles.CountAsync(f => f.BatchId == batchId, ct) >= options.MaxFilesPerBatch)
            return new(AddFileRefusal.TooManyFiles, Message: $"A bulk import takes up to {options.MaxFilesPerBatch} PDFs: start another for the rest.");

        // The upload's time (21 Nov 2026): from here (the request's body received) to the PDF kept in storage.
        var received = System.Diagnostics.Stopwatch.GetTimestamp();
        var temp = Path.Combine(Path.GetTempPath(), $"lilia-bulk-upload-{Guid.NewGuid():N}.pdf");
        try
        {
            // To disk, hashed on the way, never held whole in memory (95 MB, and the server has no swap).
            string hash;
            long length = 0;
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var local = File.Create(temp))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    length += read;
                    if (length > options.MaxFileBytes)
                        return new(AddFileRefusal.TooLarge, Message: $"A PDF may be up to {options.MaxFileBytes / (1024 * 1024)} MB.");
                    sha.AppendData(buffer, 0, read);
                    await local.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }
            if (!await IsPdfAsync(temp, ct)) return new(AddFileRefusal.NotPdf, Message: "This file is not a PDF.");

            // The exact pages, before anything leaves the machine: they size the chunks and the charge.
            if (pipeline.CountPages(temp) is not { } pages || pages <= 0)
                return new(AddFileRefusal.PagesUnknown, Message: "Lilia couldn't count this PDF's pages: is it complete?");

            // The plan and credits for this file's pages and every page of the author's still waiting to be read, so 41
            // files dropped at once can't go past the month's pages one by one.
            var waiting = await PagesWaitingAsync(userId, ct);
            await pipeline.EnsureCanImportPdfPagesAsync(userId, pages + waiting, newDocument: true, ct);

            var fileId = Guid.NewGuid();
            var key = $"bulk-imports/{userId}/{fileId:N}/source.pdf";
            await using (var upload = File.OpenRead(temp))
                await storage.UploadAsync(key, upload, "application/pdf");

            var uploadMs = (int)Math.Min(int.MaxValue, System.Diagnostics.Stopwatch.GetElapsedTime(received).TotalMilliseconds);
            var now = DateTime.UtcNow;
            db.BulkImportFiles.Add(new BulkImportFile
            {
                Id = fileId,
                BatchId = batchId,
                UserId = userId,
                OriginalName = SafeName(fileName),
                StorageKey = key,
                SourceHash = hash,
                Pages = pages,
                Status = Queued,
                UploadMs = uploadMs,
                SizeBytes = length,
                CreatedAt = now,
                UpdatedAt = now,
            });
            // The file is ONE import (the review of API #675): its own import job, made here so the next file's plan check
            // counts it, and shown as one import on the plan meter and in recent activity. Its id is the file's; the
            // processor keeps its status and document in step. The chunks' jobs are IMPORT_CHUNK and never count.
            db.Jobs.Add(new Job
            {
                Id = fileId,
                TenantId = userId,
                UserId = userId,
                JobType = JobTypes.Import,
                Status = JobStatus.Processing,
                Progress = 0,
                SourceFormat = "pdf",
                TargetFormat = "lilia",
                SourceFileName = SafeName(fileName),
                CreatedAt = now,
                UpdatedAt = now,
            });
            batch.Status = Processing;
            batch.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await queue.SendAsync(new SplitBulkImportFile(fileId));
            logger.LogInformation("[BulkImport] File {FileId} added to batch {BatchId}: {Pages} pages, {Bytes} bytes", fileId, batchId, pages, length);
            BulkImportTiming.Upload(logger, uploadMs, fileId, length, pages);
            return new(AddFileRefusal.None, await BulkImportReadModel.FileAsync(db, fileId, ct));
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The author's pages accepted but not yet charged: files not split yet, and chunks not read yet.</summary>
    internal async Task<int> PagesWaitingAsync(string userId, CancellationToken ct)
    {
        var unsplit = await db.BulkImportFiles.Where(f => f.UserId == userId && (f.Status == Queued || f.Status == Splitting))
            .SumAsync(f => (int?)f.Pages, ct) ?? 0;
        var unread = await db.BulkImportChunks
            .Where(c => c.UserId == userId && (c.Status == Queued || c.Status == Sent || c.Status == Reading) && c.ChargedPages == 0)
            .SumAsync(c => (int?)(c.LastPage - c.FirstPage + 1), ct) ?? 0;
        return unsplit + unread;
    }

    private static async Task<bool> IsPdfAsync(string path, CancellationToken ct)
    {
        // "%PDF-" within the first KB, as readers accept (a few bytes of junk may come first).
        var head = new byte[1024];
        await using var stream = File.OpenRead(path);
        var n = await stream.ReadAsync(head, ct);
        return head.AsSpan(0, n).IndexOf("%PDF-"u8) >= 0;
    }

    internal static string SafeName(string? name)
    {
        var bare = Path.GetFileName((name ?? "").Replace('\\', '/'));
        bare = new string(bare.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (string.IsNullOrEmpty(bare)) bare = "document.pdf";
        return bare.Length > 255 ? bare[..255] : bare;
    }

    public Task<IReadOnlyList<BulkImportBatchSummaryDto>> ListAsync(string userId, CancellationToken ct = default) =>
        BulkImportReadModel.BatchesAsync(db, userId, ct: ct);

    public Task<BulkImportBatchDto?> GetAsync(string userId, Guid batchId, CancellationToken ct = default) =>
        BulkImportReadModel.BatchAsync(db, userId, batchId, ct);

    /// <summary>Null: no such file of this author. False: nothing to retry.</summary>
    public async Task<(bool? Retried, BulkImportFileDto? File)> RetryFileAsync(string userId, Guid batchId, Guid fileId, CancellationToken ct = default)
    {
        var file = await db.BulkImportFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.BatchId == batchId && f.UserId == userId, ct);
        if (file is null) return (null, null);
        var retried = await processor.RetryAsync(file, ct);
        return (retried, await BulkImportReadModel.FileAsync(db, fileId, ct));
    }

    /// <summary>The timings and precision over the last <paramref name="days"/> days (staff, 21 Nov 2026).</summary>
    public Task<BulkImportStatsDto> StatsAsync(int days, CancellationToken ct = default) =>
        BulkImportStats.ComputeAsync(db, days, DateTime.UtcNow, ct);

    /// <summary>The file's read-only preview (21 Nov 2026); null when it isn't this author's.</summary>
    public Task<BulkImportPreviewDto?> PreviewAsync(string userId, Guid batchId, Guid fileId, CancellationToken ct = default) =>
        BulkImportReadModel.PreviewAsync(db, userId, batchId, fileId, ct);

    /// <summary>The file's warnings, each named and placed (21 Nov 2026); null when it isn't this author's.</summary>
    public Task<BulkImportWarningListDto?> WarningsAsync(string userId, Guid batchId, Guid fileId, CancellationToken ct = default) =>
        BulkImportReadModel.WarningsAsync(db, userId, batchId, fileId, ct);

    /// <summary>The raster types a staged figure is served as (21 Nov 2026): never SVG, HTML or anything a browser runs.</summary>
    internal static readonly string[] RasterTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    /// <summary>
    /// A staged figure of the author's file, for the preview (21 Nov 2026): its bytes and type, or null when it isn't this
    /// author's, isn't staged (any more), or isn't a raster image.
    /// </summary>
    public async Task<(Stream Content, string ContentType)?> StagedAssetAsync(string userId, Guid batchId, Guid fileId, string name, CancellationToken ct = default)
    {
        if (name.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-f]{32}\\.[a-z0-9]{1,5}$")) return null;
        var asset = await db.BulkImportStagedAssets.AsNoTracking()
            .Where(a => a.FileId == fileId && a.Name == name && a.File!.BatchId == batchId && a.File.UserId == userId)
            .Select(a => new { a.StorageKey, a.ContentType })
            .FirstOrDefaultAsync(ct);
        if (asset is null) return null;
        var type = asset.ContentType.ToLowerInvariant() == "image/jpg" ? "image/jpeg" : asset.ContentType.ToLowerInvariant();
        if (Array.IndexOf(RasterTypes, type) < 0) return null;
        try
        {
            return (await storage.DownloadAsync(asset.StorageKey), type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[BulkImport] Staged figure {Name} of file {FileId} couldn't be read", name, fileId);
            return null;
        }
    }

    /// <summary>Null: no such file of this author. False: nothing left to stop.</summary>
    public async Task<(bool? Cancelled, BulkImportFileDto? File)> CancelFileAsync(string userId, Guid batchId, Guid fileId, CancellationToken ct = default)
    {
        var file = await db.BulkImportFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.BatchId == batchId && f.UserId == userId, ct);
        if (file is null) return (null, null);
        var cancelled = await processor.CancelFileAsync(file, ct);
        return (cancelled, await BulkImportReadModel.FileAsync(db, fileId, ct));
    }

    public async Task<BulkImportBatchDto?> CancelAsync(string userId, Guid batchId, CancellationToken ct = default)
    {
        if (!await db.BulkImportBatches.AnyAsync(b => b.Id == batchId && b.UserId == userId, ct)) return null;
        await processor.CancelBatchAsync(batchId, ct);
        return await BulkImportReadModel.BatchAsync(db, userId, batchId, ct);
    }
}
