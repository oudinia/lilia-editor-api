namespace Lilia.Api.Services;

/// <summary>
/// FT-SANDBOX-SCOPE reaper. Periodically hard-deletes sandbox
/// (<c>is_playground = true</c>) documents that have been idle past the TTL,
/// so abandoned playgrounds self-clean. Mirrors
/// <see cref="TrashPurgeBackgroundService"/>. Documents has no inline
/// <c>expires_at</c>, so the expiry path is an idle-TTL on <c>UpdatedAt</c>
/// (bumped on every block/metadata write) — an actively-edited playground is
/// never reaped mid-session.
/// </summary>
public class PlaygroundPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaygroundPurgeBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int TtlHours = 24;

    public PlaygroundPurgeBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PlaygroundPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PlaygroundPurge] Background service started. TTL={TtlHours}h, Interval={IntervalHours}h",
            TtlHours, Interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

                var purgedCount = await documentService.PurgePlaygroundDocumentsAsync(TtlHours);

                if (purgedCount > 0)
                {
                    _logger.LogInformation("[PlaygroundPurge] Purged {Count} idle playground documents", purgedCount);
                }
                else
                {
                    _logger.LogDebug("[PlaygroundPurge] No idle playground documents to purge");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PlaygroundPurge] Error purging playground documents");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
