using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities.E2E;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

// =====================================================================
//  E2E scenario service. Backs the /api/e2e/* controller.
//
//  Two concerns:
//   1. Reporter ingest — start/finish runs, append results, bulk
//      coverage events. Hot path; keep allocations minimal.
//   2. Admin reads — list modules / surfaces / scenarios for the
//      in-app authoring UI. Less latency-sensitive.
//
//  See lilia-docs/launch-readiness/2026-05-18-e2e-scenario-db.md.
// =====================================================================

public interface IE2EService
{
    // Catalogue reads
    Task<List<E2EModuleDto>> ListModulesAsync(CancellationToken ct);
    Task<List<E2ESurfaceDto>> ListSurfacesAsync(string? moduleSlug, CancellationToken ct);
    Task<List<E2EBlockTypeDto>> ListBlockTypesAsync(CancellationToken ct);

    // Scenarios
    Task<List<E2EScenarioListItemDto>> ListScenariosAsync(
        string? moduleSlug, string? surfaceSlug, string? detailLevel,
        string? reviewState, string? criticality, CancellationToken ct);

    Task<E2EScenarioDetailDto?> GetScenarioAsync(string slug, CancellationToken ct);

    // Reporter ingest
    Task<StartRunResponse> StartRunAsync(StartRunRequest req, CancellationToken ct);
    Task<Guid?> RecordResultAsync(RecordResultRequest req, CancellationToken ct);
    Task FinalizeRunAsync(Guid runId, FinalizeRunRequest req, CancellationToken ct);

    // Coverage
    Task<int> IngestCoverageEventsAsync(CoverageEventsRequest req, CancellationToken ct);
    Task<List<UIElementCoverageDto>> GetUIElementCoverageAsync(
        string? moduleSlug, CancellationToken ct);
}

public class E2EService : IE2EService
{
    private readonly LiliaDbContext _db;
    private readonly ILogger<E2EService> _logger;

    public E2EService(LiliaDbContext db, ILogger<E2EService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---- Catalogue reads ----

    public async Task<List<E2EModuleDto>> ListModulesAsync(CancellationToken ct)
    {
        return await _db.E2EModules
            .Where(m => m.IsActive)
            .OrderBy(m => m.Criticality).ThenBy(m => m.Slug)
            .Select(m => new E2EModuleDto
            {
                Id = m.Id,
                Slug = m.Slug,
                Name = m.Name,
                Description = m.Description,
                Criticality = m.Criticality,
                SurfaceCount = m.Surfaces.Count(s => s.IsActive),
                ScenarioCount = m.Scenarios.Count(s => !s.IsDeleted),
            })
            .ToListAsync(ct);
    }

    public async Task<List<E2ESurfaceDto>> ListSurfacesAsync(string? moduleSlug, CancellationToken ct)
    {
        var q = _db.E2ESurfaces.Where(s => s.IsActive);
        if (!string.IsNullOrEmpty(moduleSlug))
            q = q.Where(s => s.Module.Slug == moduleSlug);

        return await q
            .OrderBy(s => s.Criticality).ThenBy(s => s.Slug)
            .Select(s => new E2ESurfaceDto
            {
                Id = s.Id,
                Slug = s.Slug,
                Name = s.Name,
                Description = s.Description,
                SurfaceKind = s.SurfaceKind,
                RoutePattern = s.RoutePattern,
                SourceFile = s.SourceFile,
                Criticality = s.Criticality,
                ModuleId = s.ModuleId,
                ModuleSlug = s.Module.Slug,
                UIElementCount = s.UIElements.Count(e => e.IsActive),
                EntryPointCount = s.EntryPoints.Count(),
                ScenarioCount = s.Scenarios.Count(sc => !sc.IsDeleted),
            })
            .ToListAsync(ct);
    }

    public async Task<List<E2EBlockTypeDto>> ListBlockTypesAsync(CancellationToken ct)
    {
        return await _db.E2EBlockTypes
            .OrderBy(b => b.Slug)
            .Select(b => new E2EBlockTypeDto
            {
                Id = b.Id,
                Slug = b.Slug,
                Name = b.Name,
                Category = b.Category,
                LatexRole = b.LatexRole,
                ScenarioCount = b.ScenarioCount,
            })
            .ToListAsync(ct);
    }

    // ---- Scenarios ----

    public async Task<List<E2EScenarioListItemDto>> ListScenariosAsync(
        string? moduleSlug, string? surfaceSlug, string? detailLevel,
        string? reviewState, string? criticality, CancellationToken ct)
    {
        var q = _db.E2EScenarios
            .Where(s => !s.IsDeleted);

        if (!string.IsNullOrEmpty(moduleSlug))
            q = q.Where(s => s.Module.Slug == moduleSlug);
        if (!string.IsNullOrEmpty(surfaceSlug))
            q = q.Where(s => s.TargetSurface != null && s.TargetSurface.Slug == surfaceSlug);
        if (!string.IsNullOrEmpty(detailLevel))
            q = q.Where(s => s.DetailLevel == detailLevel);
        if (!string.IsNullOrEmpty(reviewState))
            q = q.Where(s => s.ReviewState == reviewState);
        if (!string.IsNullOrEmpty(criticality))
            q = q.Where(s => s.Criticality == criticality);

        return await q
            .OrderBy(s => s.Criticality).ThenBy(s => s.Slug)
            .Select(s => new E2EScenarioListItemDto
            {
                Id = s.Id,
                Slug = s.Slug,
                Title = s.Title,
                ModuleSlug = s.Module.Slug,
                SurfaceSlug = s.TargetSurface != null ? s.TargetSurface.Slug : null,
                EntryPointSlug = s.EntryPoint != null ? s.EntryPoint.Slug : null,
                Criticality = s.Criticality,
                DetailLevel = s.DetailLevel,
                ReviewState = s.ReviewState,
                ExecutionMode = s.ExecutionMode,
                AutomationContent = s.AutomationContent,
                UpdatedAt = s.UpdatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<E2EScenarioDetailDto?> GetScenarioAsync(string slug, CancellationToken ct)
    {
        var scenario = await _db.E2EScenarios
            .Include(s => s.Module)
            .Include(s => s.TargetSurface)
            .Include(s => s.EntryPoint)
            .Include(s => s.CurrentVersion)
            .Include(s => s.Tags).ThenInclude(t => t.Tag)
            .FirstOrDefaultAsync(s => s.Slug == slug && !s.IsDeleted, ct);

        if (scenario is null) return null;

        var versions = await _db.E2EScenarioVersions
            .Where(v => v.ScenarioId == scenario.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Take(20)
            .ToListAsync(ct);

        return new E2EScenarioDetailDto
        {
            Id = scenario.Id,
            Slug = scenario.Slug,
            Title = scenario.Title,
            Description = scenario.Description,
            ModuleSlug = scenario.Module.Slug,
            SurfaceSlug = scenario.TargetSurface?.Slug,
            EntryPointSlug = scenario.EntryPoint?.Slug,
            Criticality = scenario.Criticality,
            DetailLevel = scenario.DetailLevel,
            ReviewState = scenario.ReviewState,
            ExecutionMode = scenario.ExecutionMode,
            AutomationContent = scenario.AutomationContent,
            EstimateSeconds = scenario.EstimateSeconds,
            Milestone = scenario.Milestone,
            ExploratoryMission = scenario.ExploratoryMission,
            ExploratoryGoals = scenario.ExploratoryGoals,
            Tags = scenario.Tags.Select(t => t.Tag.Slug).ToList(),
            UpdatedAt = scenario.UpdatedAt,
            CurrentVersion = scenario.CurrentVersion is null ? null : ToVersionDto(scenario.CurrentVersion),
            Versions = versions.Select(ToVersionDto).ToList(),
        };
    }

    private static E2EScenarioVersionDto ToVersionDto(E2EScenarioVersion v)
    {
        return new E2EScenarioVersionDto
        {
            Id = v.Id,
            VersionNumber = v.VersionNumber,
            DetailLevel = v.DetailLevel,
            Title = v.Title,
            Description = v.Description,
            Preconditions = v.Preconditions is null ? null : v.Preconditions.RootElement.Clone(),
            Steps = v.Steps.RootElement.Clone(),
            ExpectedOutcomes = v.ExpectedOutcomes is null ? null : v.ExpectedOutcomes.RootElement.Clone(),
            GenerationProvenance = v.GenerationProvenance,
            GeneratedBy = v.GeneratedBy,
            CreatedAt = v.CreatedAt,
        };
    }

    // ---- Reporter ingest ----

    public async Task<StartRunResponse> StartRunAsync(StartRunRequest req, CancellationToken ct)
    {
        var run = new E2ETestRun
        {
            TriggerKind = req.TriggerKind,
            Branch = req.Branch,
            CommitSha = req.CommitSha,
            Environment = req.Environment is null ? null
                : JsonDocument.Parse(req.Environment.Value.GetRawText()),
            StartedAt = DateTime.UtcNow,
        };
        _db.E2ETestRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("E2E run started: {RunId} (trigger={Trigger}, branch={Branch}, sha={Sha})",
            run.Id, run.TriggerKind, run.Branch ?? "—", run.CommitSha?[..8] ?? "—");
        return new StartRunResponse { Id = run.Id, StartedAt = run.StartedAt };
    }

    public async Task<Guid?> RecordResultAsync(RecordResultRequest req, CancellationToken ct)
    {
        // Resolve the scenario by automation_content. Drop the result if
        // the scenario isn't catalogued yet — better than 404'ing the
        // reporter, since a run might span newly-added tests.
        var scenario = await _db.E2EScenarios
            .Where(s => s.AutomationContent == req.AutomationContent && !s.IsDeleted)
            .Select(s => new { s.Id, s.CurrentVersionId })
            .FirstOrDefaultAsync(ct);

        if (scenario is null)
        {
            _logger.LogWarning(
                "E2E result for unknown scenario (automation_content={Ac}). Result dropped.",
                req.AutomationContent);
            return null;
        }

        if (scenario.CurrentVersionId is null)
        {
            _logger.LogWarning(
                "E2E scenario {ScenarioId} has no current version — result dropped.",
                scenario.Id);
            return null;
        }

        var result = new E2EScenarioResult
        {
            TestRunId = req.TestRunId,
            ScenarioId = scenario.Id,
            ScenarioVersionId = scenario.CurrentVersionId.Value,
            DetailLevelRun = req.DetailLevelRun,
            Outcome = req.Outcome,
            DurationMs = req.DurationMs,
            Browser = req.Browser,
            BrowserVersion = req.BrowserVersion,
            ViewportW = req.ViewportW,
            ViewportH = req.ViewportH,
            Locale = req.Locale,
            FeatureFlags = req.FeatureFlags is null ? null
                : JsonDocument.Parse(req.FeatureFlags.Value.GetRawText()),
            BackendBuildSha = req.BackendBuildSha,
            TestOrderIndex = req.TestOrderIndex,
            FailureKind = req.FailureKind,
            FailureMessage = req.FailureMessage,
            FailureStack = req.FailureStack,
            RetryAttempt = req.RetryAttempt,
            IsRetryPass = req.IsRetryPass,
            TracePath = req.TracePath,
            ScreenshotPath = req.ScreenshotPath,
            VideoPath = req.VideoPath,
            Parametrisation = req.Parametrisation is null ? null
                : JsonDocument.Parse(req.Parametrisation.Value.GetRawText()),
            RecordedAt = DateTime.UtcNow,
        };

        _db.E2EScenarioResults.Add(result);

        // Auto-promote on green. A scenario's detail_level is the
        // high-water mark: L1 (stub) → L2 (authored prose) → L3
        // (executable test). When a Playwright test passes against the
        // current version, that proves L3 — bump both scenario and the
        // referenced version. We never *downgrade* on red; a flaky
        // test failure must not blank out the L3 status that a previous
        // green earned. The scenario already has a current version
        // (checked above), so loading it is cheap.
        if (string.Equals(req.DetailLevelRun, "l3", StringComparison.OrdinalIgnoreCase)
            && string.Equals(req.Outcome, "pass", StringComparison.OrdinalIgnoreCase))
        {
            var versionId = scenario.CurrentVersionId.Value;
            var version = await _db.E2EScenarioVersions
                .Where(v => v.Id == versionId)
                .FirstOrDefaultAsync(ct);

            if (version is not null && !string.Equals(version.DetailLevel, "l3", StringComparison.OrdinalIgnoreCase))
            {
                version.DetailLevel = "l3";
            }

            var scenarioRow = await _db.E2EScenarios
                .Where(s => s.Id == scenario.Id)
                .FirstOrDefaultAsync(ct);

            if (scenarioRow is not null && !string.Equals(scenarioRow.DetailLevel, "l3", StringComparison.OrdinalIgnoreCase))
            {
                var previous = scenarioRow.DetailLevel;
                scenarioRow.DetailLevel = "l3";
                _logger.LogInformation(
                    "E2E scenario {ScenarioId} promoted {Prev}→l3 on green result {ResultId}.",
                    scenario.Id, previous, result.Id);
            }
        }

        await _db.SaveChangesAsync(ct);
        return result.Id;
    }

    public async Task FinalizeRunAsync(Guid runId, FinalizeRunRequest req, CancellationToken ct)
    {
        var run = await _db.E2ETestRuns.FindAsync(new object[] { runId }, ct);
        if (run is null) return;

        // Roll up totals from the result table — authoritative source.
        var totals = await _db.E2EScenarioResults
            .Where(r => r.TestRunId == runId)
            .GroupBy(r => r.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count(), Duration = g.Sum(r => r.DurationMs) })
            .ToListAsync(ct);

        run.EndedAt = DateTime.UtcNow;
        run.Total = totals.Sum(t => t.Count);
        run.Passed = totals.FirstOrDefault(t => t.Outcome == "pass")?.Count ?? 0;
        run.Failed = totals.FirstOrDefault(t => t.Outcome == "fail")?.Count ?? 0;
        run.Skipped = totals.FirstOrDefault(t => t.Outcome == "skip")?.Count ?? 0;
        run.DurationMs = totals.Sum(t => t.Duration);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "E2E run {RunId} finalized: total={Total} passed={Passed} failed={Failed} skipped={Skipped}",
            runId, run.Total, run.Passed, run.Failed, run.Skipped);
    }

    // ---- Coverage events ----

    public async Task<int> IngestCoverageEventsAsync(CoverageEventsRequest req, CancellationToken ct)
    {
        if (req.Events.Count == 0) return 0;

        // Resolve surface / ui_element ids in one pass for the whole
        // batch — avoids per-event round-trips.
        var surfaceSlugs = req.Events
            .Where(e => !string.IsNullOrEmpty(e.SurfaceSlug))
            .Select(e => e.SurfaceSlug!)
            .Distinct()
            .ToList();
        var elementSlugs = req.Events
            .Where(e => !string.IsNullOrEmpty(e.UIElementSlug))
            .Select(e => e.UIElementSlug!)
            .Distinct()
            .ToList();

        var surfaces = await _db.E2ESurfaces
            .Where(s => surfaceSlugs.Contains(s.Slug))
            .Select(s => new { s.Id, s.Slug, ModuleSlug = s.Module.Slug })
            .ToListAsync(ct);
        var elements = await _db.E2EUIElements
            .Where(e => elementSlugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, SurfaceSlug = e.Surface.Slug })
            .ToListAsync(ct);

        var inserted = 0;
        foreach (var ev in req.Events)
        {
            var surface = ev.SurfaceSlug is null ? null
                : surfaces.FirstOrDefault(s => s.Slug == ev.SurfaceSlug
                    && (ev.ModuleSlug is null || s.ModuleSlug == ev.ModuleSlug));
            var element = ev.UIElementSlug is null ? null
                : elements.FirstOrDefault(e => e.Slug == ev.UIElementSlug
                    && (surface is null || e.SurfaceSlug == surface.Slug));

            Guid? scenarioResultId = null;
            if (Guid.TryParse(ev.ScenarioResultId, out var parsedId)) scenarioResultId = parsedId;

            _db.E2EUIInteractionEvents.Add(new E2EUIInteractionEvent
            {
                Source = ev.Source,
                ScenarioResultId = scenarioResultId,
                RealUserSessionHash = ev.RealUserSessionHash,
                UIElementId = element?.Id,
                SurfaceId = surface?.Id,
                InteractionKind = ev.InteractionKind,
                OccurredAt = DateTime.UtcNow,
                Metadata = ev.Metadata is null ? null
                    : JsonDocument.Parse(ev.Metadata.Value.GetRawText()),
            });
            inserted++;
        }

        await _db.SaveChangesAsync(ct);
        return inserted;
    }

    public async Task<List<UIElementCoverageDto>> GetUIElementCoverageAsync(
        string? moduleSlug, CancellationToken ct)
    {
        // Pull from the matview when populated. Until first nightly
        // refresh runs, fall back to a live query that mirrors the
        // matview's definition so the admin UI works on day one.
        var q = _db.E2EUIElements
            .Where(e => e.IsActive);

        if (!string.IsNullOrEmpty(moduleSlug))
            q = q.Where(e => e.Surface.Module.Slug == moduleSlug);

        return await q
            .Select(e => new UIElementCoverageDto
            {
                UIElementId = e.Id,
                UIElementSlug = e.Slug,
                SurfaceSlug = e.Surface.Slug,
                ScenarioCount = _db.E2EScenarioCoverageLinks
                    .Count(cov => cov.TargetKind == "ui_element" && cov.TargetId == e.Id),
                LastTestExercise = _db.E2EUIInteractionEvents
                    .Where(ev => ev.UIElementId == e.Id && ev.Source == "test")
                    .Max(ev => (DateTime?)ev.OccurredAt),
                LastUserExercise = _db.E2EUIInteractionEvents
                    .Where(ev => ev.UIElementId == e.Id && ev.Source == "real_user")
                    .Max(ev => (DateTime?)ev.OccurredAt),
                RealUserInteractionCount = _db.E2EUIInteractionEvents
                    .Count(ev => ev.UIElementId == e.Id && ev.Source == "real_user"),
            })
            .OrderBy(c => c.SurfaceSlug).ThenBy(c => c.UIElementSlug)
            .ToListAsync(ct);
    }
}
