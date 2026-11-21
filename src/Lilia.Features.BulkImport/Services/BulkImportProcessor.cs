using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Lilia.Core.Entities.BulkImportStatuses;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>
/// The steps of a bulk import (21 Nov 2026), each run by one message: split a file and make its document, read a chunk
/// through Mathpix, place a read chunk into the document. Each step reads its row first and does nothing if the row says
/// it's done, so a message delivered twice is harmless.
///
/// <para><b>Fairness and cost.</b> An author has at most <c>MaxChunksPerAuthor</c> chunks sent to the ocr queue or
/// being read; the rest wait in the database (queued) and are sent as those finish (<see cref="DispatchAsync"/>), so
/// one author's 41 files never fill the queue ahead of another's and nothing churns while it waits. A chunk is sent to
/// Mathpix only after its document is checked: a deleted document cancels its file's chunks not yet sent, uncharged. A
/// chunk whose pages were read before (<see cref="MathpixChunkCache"/>) reuses that reading: no call, no charge. A chunk
/// is charged its exact pages once its reading is stored, never when it fails or is cancelled; staff are exempt (the
/// pipeline's charge).</para>
/// </summary>
public sealed class BulkImportProcessor(
    LiliaDbContext db,
    IStorageService storage,
    IPdfSplitter splitter,
    IPdfImportPipeline pipeline,
    IBulkImportQueue queue,
    IBulkImportNotifier notifier,
    BulkImportOptions options,
    IOptions<MathpixOptions> mathpixOptions,
    ILogger<BulkImportProcessor> logger,
    IMathpixClient? mathpix = null)
{
    // ── texts the author reads in the document ───────────────────────────────────────────────────────────────────

    internal static string PagesOf(BulkImportChunk c) =>
        c.FirstPage == c.LastPage ? $"Page {c.FirstPage}" : $"Pages {c.FirstPage}–{c.LastPage}";

    internal static string ReadingText(BulkImportChunk c) => $"{PagesOf(c)}: being read…";
    internal static string FailedText(BulkImportChunk c) => $"{PagesOf(c)} couldn't be read. Retry from Bulk import";
    internal static string CancelledText(BulkImportChunk c) => $"{PagesOf(c)}: not imported (cancelled).";

    /// <summary>The cache key of a chunk: the source file's SHA-256 and the chunk's pages (the same pages of the same file).</summary>
    internal static string ChunkHash(string sourceHash, int first, int last) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"bulk-chunk/v1\n{sourceHash}\n{first}-{last}")));

    internal static string ResultKeyOf(string hash) => $"bulk-imports/results/{hash}.tex.zip";

    // ── split ────────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task SplitAsync(Guid fileId, CancellationToken ct = default)
    {
        // Claimed: one splitter per file, even if the message came twice.
        var claimed = await db.BulkImportFiles.Where(f => f.Id == fileId && f.Status == Queued)
            .ExecuteUpdateAsync(f => f.SetProperty(x => x.Status, Splitting).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
        if (claimed == 0) return;
        var file = await db.BulkImportFiles.FirstAsync(f => f.Id == fileId, ct);
        if (await db.BulkImportChunks.AnyAsync(c => c.FileId == fileId, ct))
        {
            // Split already (a retry of a file whose chunks exist): carry on from the chunks.
            file.Status = Reading;
            await db.SaveChangesAsync(ct);
            await DispatchAsync(file.UserId, ct);
            return;
        }
        await NotifyAsync(file, force: true, ct);

        var work = Path.Combine(Path.GetTempPath(), "lilia-bulk-import", fileId.ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var source = Path.Combine(work, "source.pdf");
            await using (var stored = await storage.DownloadAsync(file.StorageKey))
            await using (var local = File.Create(source))
                await stored.CopyToAsync(local, ct);

            var ranges = PdfSplitter.Ranges(file.Pages, options.PagesPerChunk);
            var paths = await splitter.SplitAsync(source, ranges, Path.Combine(work, "chunks"), ct);
            File.Delete(source);

            var keys = new List<string>(paths.Count);
            foreach (var (path, range) in paths.Zip(ranges))
            {
                var key = $"bulk-imports/{file.UserId}/{file.Id:N}/chunk-{range.Index:D4}.pdf";
                await using (var chunkStream = File.OpenRead(path))
                    await storage.UploadAsync(key, chunkStream, "application/pdf");
                File.Delete(path);
                keys.Add(key);
            }

            // Cancelled while it was being split: the chunks are recorded, none is sent.
            var cancelled = await db.BulkImportFiles.AsNoTracking().Where(f => f.Id == fileId).Select(f => f.Status).FirstAsync(ct) == Cancelled;

            // The document, at once: the author can open it, and it grows as the chunks are placed. Made as the review's
            // finalize makes one (the database's defaults); the plan was checked at the upload.
            var documentId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var title = TitleOf(file.OriginalName);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO documents (id, owner_id, title, created_at, updated_at)
                VALUES ({documentId}, {file.UserId}, {title}, {now}, {now})", ct);
            foreach (var range in ranges)
            {
                var chunk = new BulkImportChunk
                {
                    Id = Guid.NewGuid(),
                    FileId = file.Id,
                    UserId = file.UserId,
                    Index = range.Index,
                    FirstPage = range.First,
                    LastPage = range.Last,
                    Status = cancelled ? Cancelled : Queued,
                    ChunkKey = keys[range.Index],
                    ContentHash = file.SourceHash is { } h ? ChunkHash(h, range.First, range.Last) : null,
                    PlaceholderBlockId = Guid.NewGuid(),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.BulkImportChunks.Add(chunk);
                db.Blocks.Add(new Block
                {
                    Id = chunk.PlaceholderBlockId!.Value,
                    DocumentId = documentId,
                    Type = "paragraph",
                    Content = Paragraph(cancelled ? CancelledText(chunk) : ReadingText(chunk)),
                    SortOrder = range.Index,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            file.DocumentId = documentId;
            if (!cancelled) file.Status = Reading;
            else file.Status = Cancelled;
            file.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("[BulkImport] File {FileId} split into {Chunks} chunk(s) of up to {PerChunk} pages; document {DocumentId}",
                file.Id, ranges.Count, options.PagesPerChunk, documentId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[BulkImport] File {FileId} couldn't be split", fileId);
            db.ChangeTracker.Clear();
            file = await db.BulkImportFiles.FirstAsync(f => f.Id == fileId, CancellationToken.None);
            file.Status = Failed;
            file.Error = Short($"This PDF couldn't be split into pages: {ex.Message}");
            file.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        await RefreshBatchAsync(file.BatchId, ct);
        await NotifyAsync(file, force: true, ct);
        await DispatchAsync(file.UserId, ct);
    }

    internal static string TitleOf(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Trim();
        return string.IsNullOrEmpty(title) ? "Imported PDF" : title.Length > 250 ? title[..250] : title;
    }

    private static JsonDocument Paragraph(string text) => JsonDocument.Parse(JsonSerializer.Serialize(new { text }));

    // ── dispatch: an author's next chunks onto the ocr queue ─────────────────────────────────────────────────────

    /// <summary>
    /// Sends the author's next queued chunks (oldest file first, in page order) to the ocr queue, so that at most
    /// <c>MaxChunksPerAuthor</c> are sent or being read. Under a lock per author, so two calls never send too many.
    /// </summary>
    public async Task DispatchAsync(string userId, CancellationToken ct = default)
    {
        List<Guid> toSend;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"lilia-bulk:" + userId}))", ct);
            var inFlight = await db.BulkImportChunks.CountAsync(c => c.UserId == userId && (c.Status == Sent || c.Status == Reading), ct);
            var room = Math.Max(1, options.MaxChunksPerAuthor) - inFlight;
            if (room <= 0) return;
            toSend = await db.BulkImportChunks
                .Where(c => c.UserId == userId && c.Status == Queued && c.File!.Status == Reading)
                .OrderBy(c => c.File!.CreatedAt).ThenBy(c => c.FileId).ThenBy(c => c.Index)
                .Select(c => c.Id)
                .Take(room)
                .ToListAsync(ct);
            if (toSend.Count == 0) return;
            var now = DateTime.UtcNow;
            await db.BulkImportChunks.Where(c => toSend.Contains(c.Id) && c.Status == Queued)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Status, Sent).SetProperty(x => x.UpdatedAt, now), ct);
            await tx.CommitAsync(ct);
        }
        foreach (var id in toSend) await queue.SendAsync(new ReadBulkImportChunk(id));
    }

    // ── read ─────────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task ReadAsync(Guid chunkId, CancellationToken ct = default)
    {
        var chunk = await db.BulkImportChunks.Include(c => c.File).FirstOrDefaultAsync(c => c.Id == chunkId, ct);
        if (chunk?.File is not { } file) return;
        var staleBefore = DateTime.UtcNow.AddMinutes(-Math.Max(1, options.StaleAfterMinutes));
        if (chunk.Status != Sent && !(chunk.Status == Reading && chunk.StartedAt < staleBefore)) return;

        // The document was deleted (Trash or erased) before these pages reached Mathpix: nothing more of this file is sent,
        // and nothing is charged.
        if (!await DocumentAliveAsync(file, ct))
        {
            await CancelUnsentAsync(file, "The document was deleted, so the pages not yet read were not imported.", placeholders: false, ct);
            return;
        }

        // Read before: the stored reading, no Mathpix call, nothing charged (the owner may revisit the charge).
        if (chunk.ContentHash is { } hash
            && await db.MathpixChunkCache.AsNoTracking().FirstOrDefaultAsync(c => c.Hash == hash, ct) is { } hit)
        {
            chunk.ResultKey = hit.ResultKey;
            chunk.FromCache = true;
            chunk.PagesCompleted = chunk.PageCount;
            chunk.Status = Read;
            chunk.Error = null;
            chunk.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("[BulkImport] Chunk {ChunkId} ({Pages}) from the cache: no Mathpix call", chunk.Id, PagesOf(chunk));
            await queue.SendAsync(new PlaceBulkImportChunk(chunk.Id));
            await NotifyAsync(file, force: true, ct);
            await DispatchAsync(file.UserId, ct);
            return;
        }

        if (mathpix is null)
        {
            await FailChunkAsync(chunk, "PDF reading is not available on this server.", ct);
            await DispatchAsync(file.UserId, ct);
            return;
        }

        // Claimed: exactly one reader, even if the message came twice.
        var now = DateTime.UtcNow;
        var claimed = await db.BulkImportChunks
            .Where(c => c.Id == chunk.Id && (c.Status == Sent || (c.Status == Reading && c.StartedAt < staleBefore)))
            .ExecuteUpdateAsync(c => c
                .SetProperty(x => x.Status, Reading)
                .SetProperty(x => x.StartedAt, now)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
        if (claimed == 0) return;
        await db.Entry(chunk).ReloadAsync(ct);

        var pdfId = chunk.MathpixPdfId; // a read cut by a restart is picked up where Mathpix has it, not paid twice
        var temp = Path.Combine(Path.GetTempPath(), $"lilia-bulk-{chunk.Id:N}.pdf");
        try
        {
            if (pdfId is null)
            {
                await using (var stored = await storage.DownloadAsync(chunk.ChunkKey ?? throw new InvalidOperationException("The chunk's pages are missing.")))
                await using (var local = File.Create(temp))
                    await stored.CopyToAsync(local, ct);
                pdfId = await mathpix.SubmitPdfAsync(temp, ct);
                chunk.MathpixPdfId = pdfId;
                await db.SaveChangesAsync(ct);
            }

            // Per-page progress: Mathpix's pages read so far (num_pages_completed), every few seconds.
            var deadline = DateTime.UtcNow.AddMinutes(Math.Max(1, options.ReadTimeoutMinutes));
            while (true)
            {
                var status = await mathpix.GetStatusAsync(pdfId, ct);
                if (status.Status == "error")
                    throw new InvalidOperationException($"Mathpix couldn't read these pages: {status.Error ?? "error"}");
                var completed = Math.Clamp(status.NumPagesCompleted ?? 0, 0, chunk.PageCount);
                if (completed != chunk.PagesCompleted)
                {
                    chunk.PagesCompleted = completed;
                    chunk.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    await NotifyAsync(file, force: false, ct);
                }
                if (status.Status == "completed") break;
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Mathpix took longer than {options.ReadTimeoutMinutes} minutes to read these pages.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.01, options.PollSeconds)), ct);
            }

            var zip = await mathpix.GetTexZipAsync(pdfId, ct, TimeSpan.FromSeconds(Math.Max(1, options.TexZipWaitSeconds)));
            if (zip is not { Length: > 0 }) throw new InvalidOperationException("Mathpix's LaTeX for these pages is empty.");

            var key = ResultKeyOf(chunk.ContentHash ?? chunk.Id.ToString("N"));
            using (var zipStream = new MemoryStream(zip))
                await storage.UploadAsync(key, zipStream, "application/zip");
            if (chunk.ContentHash is { } h)
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO mathpix_chunk_cache (hash, result_key, pages, created_at)
                    VALUES ({h}, {key}, {chunk.PageCount}, {DateTime.UtcNow})
                    ON CONFLICT (hash) DO NOTHING", ct);

            chunk.ResultKey = key;
            chunk.PagesCompleted = chunk.PageCount;
            chunk.Status = Read;
            chunk.Error = null;
            chunk.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await DeleteAtMathpixAsync(pdfId);
            await db.Entry(chunk).ReloadAsync(CancellationToken.None);
            chunk.MathpixPdfId = null;
            chunk.PagesCompleted = 0;
            chunk.UpdatedAt = DateTime.UtcNow;
            if (chunk.Attempts < Math.Max(1, options.MaxAttempts))
            {
                // Tried again after a wait (backoff); still in flight for the author's bound meanwhile.
                chunk.Status = Sent;
                chunk.Error = Short(ex.Message);
                await db.SaveChangesAsync(CancellationToken.None);
                var delay = options.RetryDelay(chunk.Attempts);
                logger.LogWarning(ex, "[BulkImport] Chunk {ChunkId} ({Pages}) attempt {Attempt} failed; again in {Delay}",
                    chunk.Id, PagesOf(chunk), chunk.Attempts, delay);
                await queue.SendAsync(new ReadBulkImportChunk(chunk.Id), delay);
                await NotifyAsync(file, force: false, CancellationToken.None);
            }
            else
            {
                logger.LogWarning(ex, "[BulkImport] Chunk {ChunkId} ({Pages}) failed after {Attempts} attempts", chunk.Id, PagesOf(chunk), chunk.Attempts);
                await FailChunkAsync(chunk, Short(ex.Message), CancellationToken.None);
                await DispatchAsync(file.UserId, CancellationToken.None);
            }
            return;
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // Charged as it is read, its exact pages, once (a message delivered twice charges nothing more). Mathpix has
        // billed them: a charge that fails is logged, never a reason to read them again.
        if (chunk.ChargedPages == 0)
        {
            try
            {
                await pipeline.ChargePdfPagesAsync(file.UserId, chunk.PageCount, CancellationToken.None);
                chunk.ChargedPages = chunk.PageCount;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[BulkImport] Could not charge {Pages} page(s) of chunk {ChunkId} to {UserId}", chunk.PageCount, chunk.Id, file.UserId);
            }
        }
        // Nothing stays at Mathpix once Lilia has the reading (as the single import).
        await DeleteAtMathpixAsync(pdfId);

        await queue.SendAsync(new PlaceBulkImportChunk(chunk.Id));
        await NotifyAsync(file, force: true, CancellationToken.None);
        await DispatchAsync(file.UserId, CancellationToken.None);
    }

    private async Task DeleteAtMathpixAsync(string? pdfId)
    {
        if (pdfId is null || mathpix is null || !mathpixOptions.Value.DeleteAfterImport) return;
        try
        {
            await mathpix.DeletePdfAsync(pdfId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[BulkImport] Could not delete PDF {PdfId} at Mathpix", pdfId);
        }
    }

    // ── place ────────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task PlaceAsync(Guid chunkId, CancellationToken ct = default)
    {
        var chunk = await db.BulkImportChunks.Include(c => c.File).FirstOrDefaultAsync(c => c.Id == chunkId, ct);
        if (chunk?.File is not { } file || chunk.Status != Read) return;

        if (!await DocumentAliveAsync(file, ct))
        {
            chunk.Status = Cancelled;
            chunk.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await CancelUnsentAsync(file, "The document was deleted, so the pages not yet read were not imported.", placeholders: false, ct);
            return;
        }

        try
        {
            byte[] zip;
            await using (var stored = await storage.DownloadAsync(chunk.ResultKey!))
            using (var buffer = new MemoryStream())
            {
                await stored.CopyToAsync(buffer, ct);
                zip = buffer.ToArray();
            }
            var placed = await pipeline.PlaceLatexProjectAsync(file.UserId, file.OriginalName, file.DocumentId!.Value,
                chunk.PlaceholderBlockId, applyPreamble: !file.PreambleApplied, zip, file.StorageKey, ct);

            db.ChangeTracker.Clear();
            chunk = await db.BulkImportChunks.Include(c => c.File).FirstAsync(c => c.Id == chunkId, CancellationToken.None);
            file = chunk.File!;
            chunk.Status = Done;
            chunk.Error = null;
            chunk.PlaceholderBlockId = null;
            chunk.UpdatedAt = DateTime.UtcNow;
            file.PreambleApplied = true;
            file.Warnings += placed.Warnings;
            var chunkKey = chunk.ChunkKey;
            chunk.ChunkKey = null;
            await db.SaveChangesAsync(CancellationToken.None);
            if (chunkKey is not null)
            {
                try { await storage.DeleteAsync(chunkKey); }
                catch (Exception ex) { logger.LogWarning(ex, "[BulkImport] Could not delete chunk file {Key}", chunkKey); }
            }
            await pipeline.NotifyDocumentChangedAsync(file.DocumentId!.Value, placed.BlockIds, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[BulkImport] Chunk {ChunkId} ({Pages}) was read but couldn't be placed", chunkId, PagesOf(chunk));
            db.ChangeTracker.Clear();
            chunk = await db.BulkImportChunks.Include(c => c.File).FirstAsync(c => c.Id == chunkId, CancellationToken.None);
            file = chunk.File!;
            await FailChunkAsync(chunk, Short($"Read, but couldn't be put into the document: {ex.Message}"), CancellationToken.None);
            return;
        }

        await RefreshFileAsync(file, CancellationToken.None);
        await NotifyAsync(file, force: true, CancellationToken.None);
    }

    // ── failures, cancelling, retrying ───────────────────────────────────────────────────────────────────────────

    private async Task FailChunkAsync(BulkImportChunk chunk, string error, CancellationToken ct)
    {
        chunk.Status = Failed;
        chunk.Error = Short(error);
        chunk.PagesCompleted = 0;
        chunk.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var file = chunk.File ?? await db.BulkImportFiles.FirstAsync(f => f.Id == chunk.FileId, ct);
        await SetPlaceholderAsync(file, chunk, FailedText(chunk), ct);
        await RefreshFileAsync(file, ct);
        await NotifyAsync(file, force: true, ct);
    }

    /// <summary>A chunk's placeholder says something else (failed, cancelled, being read again); an open editor reloads.</summary>
    private async Task SetPlaceholderAsync(BulkImportFile file, BulkImportChunk chunk, string text, CancellationToken ct)
    {
        if (chunk.PlaceholderBlockId is not { } blockId || file.DocumentId is not { } documentId) return;
        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(new { text });
        var changed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE blocks SET content = CAST({json} AS jsonb), updated_at = {now} WHERE id = {blockId} AND document_id = {documentId}", ct);
        if (changed == 0) return;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE documents SET version = version + 1, updated_at = {now} WHERE id = {documentId}", ct);
        await pipeline.NotifyDocumentChangedAsync(documentId, [blockId], ct);
    }

    /// <summary>
    /// The file's chunks not yet sent to Mathpix are cancelled (queued, or sent and not claimed): never read, never
    /// charged. With <paramref name="placeholders"/>, their placeholders say so.
    /// </summary>
    internal async Task CancelUnsentAsync(BulkImportFile file, string reason, bool placeholders, CancellationToken ct)
    {
        var unsent = await db.BulkImportChunks.Where(c => c.FileId == file.Id && (c.Status == Queued || c.Status == Sent)).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var c in unsent)
        {
            c.Status = Cancelled;
            c.UpdatedAt = now;
        }
        if (file.Status is Queued or Splitting)
        {
            file.Status = Cancelled;
            file.Error = reason;
        }
        file.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (placeholders)
            foreach (var c in unsent) await SetPlaceholderAsync(file, c, CancelledText(c), ct);
        if (unsent.Count > 0)
            logger.LogInformation("[BulkImport] File {FileId}: {Count} chunk(s) cancelled before Mathpix ({Reason})", file.Id, unsent.Count, reason);
        await RefreshFileAsync(file, ct, reason);
        await NotifyAsync(file, force: true, ct);
        await DispatchAsync(file.UserId, ct);
    }

    /// <summary>
    /// Reads again a file's failed and cancelled chunks (a chunk already read is only placed again), or splits it again
    /// if it never was. Null when there is nothing to retry; throws <see cref="InvalidOperationException"/> when the
    /// document is gone. The plan is checked for the pages to be read again.
    /// </summary>
    public async Task<bool> RetryAsync(BulkImportFile file, CancellationToken ct = default)
    {
        var chunks = await db.BulkImportChunks.Where(c => c.FileId == file.Id).OrderBy(c => c.Index).ToListAsync(ct);
        if (chunks.Count == 0)
        {
            if (file.Status is not (Failed or Cancelled)) return false;
            await pipeline.EnsureCanImportPdfPagesAsync(file.UserId, file.Pages, newDocument: true, ct);
            file.Status = Queued;
            file.Error = null;
            file.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await queue.SendAsync(new SplitBulkImportFile(file.Id));
            await RefreshBatchAsync(file.BatchId, ct);
            await NotifyAsync(file, force: true, ct);
            return true;
        }

        var again = chunks.Where(c => c.Status is Failed or Cancelled).ToList();
        if (again.Count == 0) return false;
        if (!await DocumentAliveAsync(file, ct))
            throw new InvalidOperationException("This file's document was deleted: import the PDF again.");
        var toRead = again.Where(c => c.ResultKey is null).Sum(c => c.PageCount);
        if (toRead > 0) await pipeline.EnsureCanImportPdfPagesAsync(file.UserId, toRead, newDocument: false, ct);

        var now = DateTime.UtcNow;
        var toPlace = new List<Guid>();
        foreach (var c in again)
        {
            c.Error = null;
            c.UpdatedAt = now;
            if (c.ResultKey is not null)
            {
                c.Status = Read;
                c.PagesCompleted = c.PageCount;
                toPlace.Add(c.Id);
            }
            else
            {
                c.Status = Queued;
                c.Attempts = 0;
                c.MathpixPdfId = null;
                c.PagesCompleted = 0;
            }
        }
        file.Status = Reading;
        file.Error = null;
        file.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        foreach (var c in again) await SetPlaceholderAsync(file, c, ReadingText(c), ct);
        foreach (var id in toPlace) await queue.SendAsync(new PlaceBulkImportChunk(id));
        await RefreshBatchAsync(file.BatchId, ct);
        await NotifyAsync(file, force: true, ct);
        await DispatchAsync(file.UserId, ct);
        return true;
    }

    /// <summary>Stops a batch: files not split are cancelled, chunks not yet sent to Mathpix too. Chunks being read finish.</summary>
    public async Task CancelBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var files = await db.BulkImportFiles.Where(f => f.BatchId == batchId).ToListAsync(ct);
        foreach (var file in files.Where(f => Array.IndexOf(FileFinished, f.Status) < 0))
        {
            if (file.Status is Queued or Splitting)
            {
                file.Status = Cancelled;
                file.Error = "Cancelled.";
                file.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await NotifyAsync(file, force: true, ct);
                continue;
            }
            await CancelUnsentAsync(file, "Cancelled.", placeholders: true, ct);
        }
        await RefreshBatchAsync(batchId, ct);
    }

    // ── status ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<bool> DocumentAliveAsync(BulkImportFile file, CancellationToken ct) =>
        file.DocumentId is { } id && await db.Documents.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(d => d.Id == id && d.DeletedAt == null, ct);

    /// <summary>
    /// A split file's status from its chunks: reading while any is unfinished; then done (all placed), failed (none
    /// placed, some failed), cancelled (none placed, none failed), or partial.
    /// </summary>
    internal async Task RefreshFileAsync(BulkImportFile file, CancellationToken ct, string? reason = null)
    {
        if (file.Status is Queued or Splitting) return;
        var statuses = await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == file.Id).Select(c => c.Status).ToListAsync(ct);
        if (statuses.Count == 0) return;
        var status = FileStatusOf(statuses);
        if (status != file.Status)
        {
            file.Status = status;
            if (status == Done) file.Error = null;
            else if (status == Cancelled && reason is not null) file.Error = reason;
            else if (status is Partial or Failed)
                file.Error = statuses.Count(s => s == Failed) is var n and > 0
                    ? $"{n} part{(n == 1 ? "" : "s")} couldn't be read. Retry from Bulk import."
                    : reason;
            file.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        await RefreshBatchAsync(file.BatchId, ct);
    }

    internal static string FileStatusOf(IReadOnlyCollection<string> chunkStatuses)
    {
        if (chunkStatuses.Any(s => Array.IndexOf(ChunkFinished, s) < 0)) return Reading;
        var done = chunkStatuses.Count(s => s == Done);
        var failed = chunkStatuses.Count(s => s == Failed);
        if (done == chunkStatuses.Count) return Done;
        if (done == 0 && failed > 0 && failed == chunkStatuses.Count) return Failed;
        if (done == 0 && failed == 0) return Cancelled;
        return Partial;
    }

    private async Task RefreshBatchAsync(Guid batchId, CancellationToken ct)
    {
        var statuses = await db.BulkImportFiles.AsNoTracking().Where(f => f.BatchId == batchId).Select(f => f.Status).ToListAsync(ct);
        var status = BulkImportReadModel.BatchStatus(statuses);
        await db.BulkImportBatches.Where(b => b.Id == batchId && b.Status != status)
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.Status, status).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }

    private async Task NotifyAsync(BulkImportFile file, bool force, CancellationToken ct)
    {
        if (await BulkImportReadModel.FileAsync(db, file.Id, ct) is { } dto)
            await notifier.FileChangedAsync(file.UserId, file.BatchId, dto, force);
    }

    internal static string Short(string message) => message.Length > 1000 ? message[..1000] : message;
}
