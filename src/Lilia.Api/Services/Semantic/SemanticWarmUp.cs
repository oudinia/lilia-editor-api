namespace Lilia.Api.Services.Semantic;

/// <summary>
/// Loads the model and the examples' vectors after the API starts (about 3 s), in the background, so
/// the first Ask Lilia message doesn't wait for them.
/// </summary>
public sealed class SemanticWarmUp(IAskLiliaRouter router, ILogger<SemanticWarmUp> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() =>
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var route = router.Route("Where do I change the page margins?");
            logger.LogInformation("[Semantic] routing ready in {Ms} ms ({Reason})", clock.ElapsedMilliseconds, route.Reason);
        }
        catch (Exception e) { logger.LogWarning(e, "[Semantic] warm-up failed: keyword routing only"); }
    }, stoppingToken);
}
