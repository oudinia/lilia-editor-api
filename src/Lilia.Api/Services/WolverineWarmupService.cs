using System.Diagnostics;
using Wolverine.Runtime;

namespace Lilia.Api.Services;

/// <summary>
/// Compiles Wolverine's message handlers right after start-up, in the background (6 Nov 2026).
///
/// <para>Wolverine generates and compiles each handler chain the first time a message of that type
/// is handled. The first sign-up after every start waited for it: the sign-in sync publishes
/// <c>UserCreatedEvent</c>, and its chain took about five seconds to build, so a new author's first
/// document sat on "Creating…" for eight (launch bot issue 1). Building every chain once the app has
/// started moves that wait off the first person.</para>
/// </summary>
public sealed class WolverineWarmupService(
    IWolverineRuntime runtime,
    IHostApplicationLifetime lifetime,
    ILogger<WolverineWarmupService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(() => Warm(stoppingToken), stoppingToken));
        return Task.CompletedTask;
    }

    private void Warm(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        int built = 0, failed = 0;
        var chains = runtime is WolverineRuntime concrete ? concrete.Handlers.Chains : [];
        foreach (var chain in chains)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                runtime.FindInvoker(chain.MessageType);
                built++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "[Warmup] Handler for {MessageType} not built ahead", chain.MessageType.Name);
            }
        }
        logger.LogInformation("[Warmup] {Built} message handlers built in {Ms} ms ({Failed} failed)", built, watch.ElapsedMilliseconds, failed);
    }
}
