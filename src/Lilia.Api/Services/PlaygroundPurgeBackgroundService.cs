namespace Lilia.Api.Services;

/// <summary>
/// FT-SANDBOX-SCOPE reaper. Periodically hard-deletes sandbox
/// (<c>is_playground = true</c>) documents AND teams that have been idle past
/// the TTL, so abandoned playgrounds self-clean. Mirrors
/// <see cref="TrashPurgeBackgroundService"/>. Neither documents nor teams has
/// an inline <c>expires_at</c>, so the expiry path is an idle-TTL on
/// <c>UpdatedAt</c> (bumped on every write) — an actively-used playground is
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
                var teamService = scope.ServiceProvider.GetRequiredService<ITeamService>();

                var purgedDocs = await documentService.PurgePlaygroundDocumentsAsync(TtlHours);
                var purgedTeams = await teamService.PurgePlaygroundTeamsAsync(TtlHours);

                if (purgedDocs > 0 || purgedTeams > 0)
                {
                    _logger.LogInformation("[PlaygroundPurge] Purged {Docs} idle playground documents, {Teams} idle playground teams", purgedDocs, purgedTeams);
                }
                else
                {
                    _logger.LogDebug("[PlaygroundPurge] No idle playground documents or teams to purge");
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
