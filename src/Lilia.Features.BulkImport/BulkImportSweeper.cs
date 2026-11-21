using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Lilia.Core.Entities.BulkImportStatuses;

namespace Lilia.Api.Features.BulkImport;

/// <summary>
/// Picks up bulk import work a restart cut short (21 Nov 2026). The queues are durable, but a step stopped halfway leaves
/// its row saying so: a chunk "reading" past <c>StaleAfterMinutes</c> is sent again (and resumes Mathpix's pdf_id rather
/// than paying for a new reading), a chunk read and not placed is placed, a file stuck splitting is split again, and an
/// author with chunks waiting and none in flight gets their next ones sent. Every <c>SweepEverySeconds</c>; 0 turns it off.
/// </summary>
public sealed class BulkImportSweeper(IServiceScopeFactory scopes, BulkImportOptions options, ILogger<BulkImportSweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.SweepEverySeconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SweepEverySeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "[BulkImport] The sweep failed");
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IBulkImportQueue>();
        var processor = scope.ServiceProvider.GetRequiredService<BulkImportProcessor>();
        var stale = DateTime.UtcNow.AddMinutes(-Math.Max(1, options.StaleAfterMinutes));

        var reading = await db.BulkImportChunks.AsNoTracking()
            .Where(c => (c.Status == Reading && c.StartedAt < stale) || (c.Status == Sent && c.UpdatedAt < stale))
            .Select(c => c.Id).Take(200).ToListAsync(ct);
        foreach (var id in reading) await queue.SendAsync(new ReadBulkImportChunk(id));

        var read = await db.BulkImportChunks.AsNoTracking()
            .Where(c => c.Status == Read && c.UpdatedAt < stale)
            .Select(c => c.Id).Take(200).ToListAsync(ct);
        foreach (var id in read) await queue.SendAsync(new PlaceBulkImportChunk(id));

        var splitting = await db.BulkImportFiles
            .Where(f => (f.Status == Splitting || f.Status == Queued) && f.UpdatedAt < stale)
            .Take(50).ToListAsync(ct);
        foreach (var f in splitting)
        {
            f.Status = Queued;
            f.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        foreach (var f in splitting) await queue.SendAsync(new SplitBulkImportFile(f.Id));

        var waiting = await db.BulkImportChunks.AsNoTracking()
            .Where(c => c.Status == Queued && c.UpdatedAt < stale)
            .Select(c => c.UserId).Distinct().Take(100).ToListAsync(ct);
        foreach (var userId in waiting) await processor.DispatchAsync(userId, ct);

        var total = reading.Count + read.Count + splitting.Count;
        if (total > 0)
            logger.LogInformation("[BulkImport] Sweep: {Reading} chunk(s) read again, {Read} placed, {Split} file(s) split again",
                reading.Count, read.Count, splitting.Count);
    }
}
