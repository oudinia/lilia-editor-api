using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Read-mostly view over the Typst translation catalog. Mirrors
/// <c>LatexCatalogService</c>'s shape — DB-driven authoritative source,
/// joined to <c>import_telemetry_events</c> at report time so the admin
/// page shows shipped-handlers vs. silent-fallback hits side by side.
///
/// Internal-only until post-launch when we add a public surface.
/// </summary>
public interface ITypstCoverageService
{
    Task<TypstCoverageReport> GetReportAsync(TimeSpan? eventWindow = null, CancellationToken ct = default);
}

public class TypstCoverageService : ITypstCoverageService
{
    private readonly LiliaDbContext _db;
    private readonly ILogger<TypstCoverageService> _logger;

    public TypstCoverageService(LiliaDbContext db, ILogger<TypstCoverageService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TypstCoverageReport> GetReportAsync(TimeSpan? eventWindow = null, CancellationToken ct = default)
    {
        var window = eventWindow ?? TimeSpan.FromDays(7);
        var since = DateTime.UtcNow.Subtract(window);

        var handlers = await _db.TypstTranslationHandlers.AsNoTracking()
            .OrderBy(h => h.Category).ThenBy(h => h.HandlerKey)
            .ToListAsync(ct);

        var gaps = await _db.TypstTranslationGaps.AsNoTracking()
            .OrderBy(g => g.MitigationStatus).ThenBy(g => g.BlockingSeverity).ThenBy(g => g.GapKey)
            .ToListAsync(ct);

        // Recent fallback hits — group by token_or_env so we can JOIN
        // to gap_key on the client side. source_format='typst' filters
        // out parser-side import telemetry that uses other formats.
        var fallbackHits = await _db.ImportTelemetryEvents.AsNoTracking()
            .Where(e => e.SourceFormat == "typst"
                     && e.EventKind == "silent_fallback"
                     && e.CreatedAt >= since)
            .GroupBy(e => e.TokenOrEnv)
            .Select(g => new { Token = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(50)
            .ToListAsync(ct);

        var totalFallbacks = fallbackHits.Sum(h => h.Count);

        var byCategory = handlers
            .GroupBy(h => h.Category)
            .ToDictionary(g => g.Key, g => new CategoryRollup(
                Category: g.Key,
                HandlerCount: g.Count(),
                ActiveCount: g.Count(h => h.Status == "active"),
                DeprecatedCount: g.Count(h => h.Status == "deprecated"),
                PlannedCount: g.Count(h => h.Status == "planned")));

        return new TypstCoverageReport(
            HandlerCount: handlers.Count,
            HandlersActive: handlers.Count(h => h.Status == "active"),
            GapCount: gaps.Count,
            GapsOpen: gaps.Count(g => g.MitigationStatus == "none"),
            GapsScheduled: gaps.Count(g => g.MitigationStatus == "scheduled"),
            GapsShipped: gaps.Count(g => g.MitigationStatus == "shipped"),
            FallbackEventsLastWindow: totalFallbacks,
            EventWindow: window,
            ByCategory: byCategory.Values.ToList(),
            TopFallbackTokens: fallbackHits
                .Select(h => new FallbackHitSummary(h.Token ?? "(null)", h.Count))
                .ToList(),
            Handlers: handlers,
            Gaps: gaps);
    }
}

public sealed record TypstCoverageReport(
    int HandlerCount,
    int HandlersActive,
    int GapCount,
    int GapsOpen,
    int GapsScheduled,
    int GapsShipped,
    int FallbackEventsLastWindow,
    TimeSpan EventWindow,
    List<CategoryRollup> ByCategory,
    List<FallbackHitSummary> TopFallbackTokens,
    List<TypstTranslationHandler> Handlers,
    List<TypstTranslationGap> Gaps);

public sealed record CategoryRollup(
    string Category,
    int HandlerCount,
    int ActiveCount,
    int DeprecatedCount,
    int PlannedCount);

public sealed record FallbackHitSummary(string Token, int Count);
