using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Keeps the standalone-tools tables bounded: prunes old <c>tool_artifacts</c>
/// (the "keep, then drop" retention from strategy §9) and old <c>tool_events</c>.
/// Daily, set-based <c>ExecuteDelete</c>; retention is config-tunable.
/// </summary>
public class ToolArtifactPruneService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ToolArtifactPruneService> _logger;
    private readonly int _artifactDays;
    private readonly int _eventDays;

    public ToolArtifactPruneService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ToolArtifactPruneService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _artifactDays = config.GetValue("Tools:ArtifactRetentionDays", 30); // heavy payload — short TTL
        _eventDays = config.GetValue("Tools:EventRetentionDays", 90);        // funnel — keep longer
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Don't compete with startup work.
        try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await PruneAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "[ToolPrune] prune pass failed"); }
            try { await Task.Delay(Interval, ct); } catch { break; }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var artCutoff = DateTime.UtcNow.AddDays(-_artifactDays);
        var evtCutoff = DateTime.UtcNow.AddDays(-_eventDays);
        var arts = await db.ToolArtifacts.Where(a => a.CreatedAt < artCutoff).ExecuteDeleteAsync(ct);
        var evts = await db.ToolEvents.Where(e => e.CreatedAt < evtCutoff).ExecuteDeleteAsync(ct);
        if (arts > 0 || evts > 0)
            _logger.LogInformation("[ToolPrune] removed {Arts} artifacts (>{AD}d) + {Evts} events (>{ED}d)",
                arts, _artifactDays, evts, _eventDays);
    }
}
