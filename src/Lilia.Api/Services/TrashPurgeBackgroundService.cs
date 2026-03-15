namespace Lilia.Api.Services;

public class TrashPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrashPurgeBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int RetentionDays = 30;

    public TrashPurgeBackgroundService(IServiceScopeFactory scopeFactory, ILogger<TrashPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TrashPurge] Background service started. Retention={RetentionDays} days, Interval={IntervalHours}h",
            RetentionDays, Interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

                var purgedCount = await documentService.PurgeExpiredDocumentsAsync(RetentionDays);

                if (purgedCount > 0)
                {
                    _logger.LogInformation("[TrashPurge] Purged {Count} expired documents", purgedCount);
                }
                else
                {
                    _logger.LogDebug("[TrashPurge] No expired documents to purge");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TrashPurge] Error purging expired documents");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
