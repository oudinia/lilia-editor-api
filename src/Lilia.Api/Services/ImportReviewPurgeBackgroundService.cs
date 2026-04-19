using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Stale-cart sweep for import review sessions. Mirrors
/// <see cref="TrashPurgeBackgroundService"/> exactly — ExecuteDeleteAsync
/// with a Where filter, no rows transit the app layer, cascade drops the
/// children (block reviews, diagnostics, comments, activities, collaborators).
///
/// Finalized sessions (Status = 'imported') are preserved for audit — only
/// stale drafts + cancelled imports get cleaned.
/// </summary>
public class ImportReviewPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportReviewPurgeBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int RetentionDays = 30;

    public ImportReviewPurgeBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ImportReviewPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ImportReviewPurge] Background service started. Retention={Retention} days, Interval={Interval}h",
            RetentionDays, Interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

                var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
                var purged = await context.ImportReviewSessions
                    .Where(s => s.Status != "imported"
                                && (s.ExpiresAt != null
                                    ? s.ExpiresAt < DateTime.UtcNow
                                    : s.CreatedAt < cutoff))
                    .ExecuteDeleteAsync(stoppingToken);

                if (purged > 0)
                    _logger.LogInformation("[ImportReviewPurge] Purged {Count} stale import review sessions", purged);
                else
                    _logger.LogDebug("[ImportReviewPurge] No stale sessions to purge");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ImportReviewPurge] Sweep failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
