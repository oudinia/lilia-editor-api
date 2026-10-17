using Lilia.Infrastructure.Data;

namespace Lilia.Api.Services;

/// <summary>
/// Keeps block_shapes current (the admin console's view of a block: its shape, never its text).
/// Database triggers queue each block written; this drains the queue, at once while there is a
/// backlog (the first run shapes every existing block) and every 2 seconds otherwise.
/// </summary>
public sealed class BlockShapeWorker(IServiceScopeFactory scopes, ILogger<BlockShapeWorker> log) : BackgroundService
{
    private const int Batch = 200;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var done = 0;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
                done = await BlockShapeProcessor.ProcessAsync(db, Batch, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Block shapes: a batch failed; retrying in 10 s");
                await Task.Delay(TimeSpan.FromSeconds(10), stop).ContinueWith(_ => { });
                continue;
            }
            if (done < Batch)
                await Task.Delay(TimeSpan.FromSeconds(2), stop).ContinueWith(_ => { });
        }
    }
}
