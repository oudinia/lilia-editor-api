using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lilia.Api.Services;

/// <summary>
/// FT-IMP-001 stage 9 — retention + archive for the import domain.
///
/// Policy (from spec §Retention — 30 days uniformly): every instance
/// with <c>UpdatedAt &lt; NOW() - 30 days</c> is considered expired,
/// regardless of status. Before deletion, a one-row summary lands in
/// <c>import_archive_stats</c> so fleet-wide analytics survive the
/// purge without retaining per-user content. Definitions outlive
/// their instances and are pruned after their last instance goes.
///
/// Runs daily. Idempotent — a re-run the same day finds nothing to do
/// because the purged rows are already gone.
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
        _logger.LogInformation("[ImportReviewPurge] Started. Retention={Retention}d, Interval={Interval}h — archive-then-delete policy",
            RetentionDays, Interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ImportReviewPurge] Sweep failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        // Find expired instances. The UpdatedAt heartbeat is what matters —
        // an active review resets the clock; an abandoned one counts toward
        // purge from its last edit.
        var expired = await db.ImportReviewSessions
            .Where(s => s.UpdatedAt < cutoff)
            .Select(s => new
            {
                s.Id,
                s.DefinitionId,
                s.OwnerId,
                s.SourceFormat,
                s.Status,
                s.DocumentCategory,
                s.QualityScore,
                s.CreatedAt,
                s.UpdatedAt,
            })
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            _logger.LogDebug("[ImportReviewPurge] Nothing expired");
            return;
        }

        _logger.LogInformation("[ImportReviewPurge] {Count} expired instances — archiving then deleting", expired.Count);

        foreach (var inst in expired)
        {
            // Gather per-instance aggregate stats. Cheap — two small
            // grouped queries per expired instance. With 30-day retention
            // the daily set is bounded.
            var byType = await db.ImportBlockReviews
                .Where(br => br.SessionId == inst.Id)
                .GroupBy(br => br.CurrentType ?? br.OriginalType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var totalBlocks = byType.Sum(x => x.Count);

            var diagBySeverity = await db.ImportDiagnostics
                .Where(d => d.SessionId == inst.Id)
                .GroupBy(d => d.Severity)
                .Select(g => new { Severity = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var errorCount = diagBySeverity.FirstOrDefault(x => x.Severity == "error")?.Count ?? 0;
            var warningCount = diagBySeverity.FirstOrDefault(x => x.Severity == "warning")?.Count ?? 0;

            var totalUsage = await db.LatexTokenUsages
                .Where(u => u.SessionId == inst.Id)
                .SumAsync(u => (int?)u.Count, ct) ?? 0;
            double? coveragePercent = null;
            if (totalUsage > 0)
            {
                var coveredUsage = await db.LatexTokenUsages
                    .Where(u => u.SessionId == inst.Id)
                    .Join(db.LatexTokens.Where(t => t.CoverageLevel == "full" || t.CoverageLevel == "partial" || t.CoverageLevel == "shimmed"),
                        u => u.TokenId, t => t.Id, (u, _) => u.Count)
                    .SumAsync(c => (int?)c, ct) ?? 0;
                coveragePercent = Math.Round(100.0 * coveredUsage / totalUsage, 1);
            }
            var unsupportedCount = await db.LatexTokenUsages
                .Where(u => u.SessionId == inst.Id)
                .Join(db.LatexTokens.Where(t => t.CoverageLevel == "unsupported" || t.CoverageLevel == "none"),
                    u => u.TokenId, t => t.Id, (u, _) => u)
                .CountAsync(ct);

            // finalState: abandoned for non-terminal rows that timed out
            // (no explicit cancel / supersede / import). Preserves the
            // real state when there is one.
            var finalState = inst.Status is "imported" or "cancelled" or "superseded"
                ? inst.Status
                : "abandoned";

            var blockCountsJson = JsonDocument.Parse(
                JsonSerializer.Serialize(byType.ToDictionary(x => x.Type, x => x.Count)));

            db.ImportArchiveStats.Add(new ImportArchiveStats
            {
                Id = Guid.NewGuid(),
                InstanceId = inst.Id,
                DefinitionId = inst.DefinitionId,
                OwnerId = inst.OwnerId,
                SourceFormat = inst.SourceFormat,
                DocumentClass = inst.DocumentCategory,
                FinalState = finalState,
                TotalBlocks = totalBlocks,
                BlockCountsByType = blockCountsJson,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                QualityScore = inst.QualityScore,
                CoverageMappedPercent = coveragePercent,
                UnsupportedTokenCount = unsupportedCount,
                InstanceCreatedAt = inst.CreatedAt,
                InstanceLastActivityAt = inst.UpdatedAt,
                ArchivedAt = DateTime.UtcNow,
                LifetimeMinutes = Math.Round((inst.UpdatedAt - inst.CreatedAt).TotalMinutes, 1),
            });
        }
        await db.SaveChangesAsync(ct);

        // Cascade delete via FK — ImportReviewSession cascades to
        // ImportBlockReviews, ImportDiagnostics, ImportReviewActivities,
        // ImportBlockComments, ImportReviewCollaborators, and the new
        // rev_documents (→ rev_blocks / rev_assets / rev_bibliography).
        var expiredIds = expired.Select(e => e.Id).ToList();
        var purged = await db.ImportReviewSessions
            .Where(s => expiredIds.Contains(s.Id))
            .ExecuteDeleteAsync(ct);

        // Prune orphan definitions — those whose last instance just went.
        var orphanDefinitionIds = expired
            .Where(e => e.DefinitionId.HasValue)
            .Select(e => e.DefinitionId!.Value)
            .Distinct()
            .ToList();
        var orphansPurged = 0;
        if (orphanDefinitionIds.Count > 0)
        {
            orphansPurged = await db.ImportDefinitions
                .Where(d => orphanDefinitionIds.Contains(d.Id)
                            && !db.ImportReviewSessions.Any(s => s.DefinitionId == d.Id))
                .ExecuteDeleteAsync(ct);
        }

        _logger.LogInformation(
            "[ImportReviewPurge] Archived+deleted {Purged} instances; pruned {Orphans} orphan definitions",
            purged, orphansPurged);
    }
}
