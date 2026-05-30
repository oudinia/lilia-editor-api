using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Daily purge of stale diagnostic captures.
///
/// Policy: anything older than <see cref="RetentionDays"/> is dropped.
/// The bundles are debug shares — useful for "minutes to days" of
/// follow-up; nothing in them survives the source code's git history,
/// so a fixed retention is fine. A future per-user opt-out can ride
/// on a single column flip without changing the sweep logic.
///
/// Idempotent: re-running mid-day finds nothing to do once the
/// cutoff rows are gone.
/// </summary>
public class DiagnosticCapturePurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiagnosticCapturePurgeBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    /// <summary>
    /// Retention window in days. Aligned with the import-review purge
    /// (also 30) so we don't have to maintain two cleanup schedules.
    /// </summary>
    public const int RetentionDays = 30;

    public DiagnosticCapturePurgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DiagnosticCapturePurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DiagnosticCapturePurge] Started. Retention={Retention}d, Interval={Interval}h",
            RetentionDays, Interval.TotalHours);

        // Sleep a small jitter on first start so a multi-instance
        // deploy doesn't have every node trying to delete the same
        // rows at exactly the same wall-clock.
        var jitterSeconds = Random.Shared.Next(0, 300);
        if (jitterSeconds > 0)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(jitterSeconds), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DiagnosticCapturePurge] Sweep failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        // ExecuteDeleteAsync compiles to a single DELETE in Postgres
        // (EF Core 8+) so we don't materialise expired rows just to
        // drop them. Returns the affected count.
        var deleted = await db.DiagnosticCaptures
            .Where(c => c.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "[DiagnosticCapturePurge] Deleted {Count} captures older than {Cutoff:yyyy-MM-dd}",
                deleted, cutoff);
        }
        else
        {
            _logger.LogDebug(
                "[DiagnosticCapturePurge] No captures older than {Cutoff:yyyy-MM-dd}",
                cutoff);
        }
    }
}
