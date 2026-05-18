using System.Text.Json;

namespace Lilia.Core.DTOs;

// =====================================================================
//  DTOs for the E2E scenario API.
//
//  Separate from entities so the public contract can evolve without
//  touching the schema. Keep these flat and JSON-friendly.
// =====================================================================

// ---- Catalogue reads ----

public class E2EModuleDto
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Criticality { get; set; } = "p1";
    public int SurfaceCount { get; set; }
    public int ScenarioCount { get; set; }
}

public class E2ESurfaceDto
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SurfaceKind { get; set; } = "page";
    public string? RoutePattern { get; set; }
    public string? SourceFile { get; set; }
    public string Criticality { get; set; } = "p1";
    public Guid ModuleId { get; set; }
    public string ModuleSlug { get; set; } = string.Empty;
    public int UIElementCount { get; set; }
    public int EntryPointCount { get; set; }
    public int ScenarioCount { get; set; }
}

public class E2EUIElementDto
{
    public Guid Id { get; set; }
    public Guid SurfaceId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ElementKind { get; set; } = "button";
    public string? AccessibleName { get; set; }
    public string? Role { get; set; }
    public string? VisibleText { get; set; }
    public string? DefaultSelector { get; set; }
    public Guid? ProducesBlockTypeId { get; set; }
    public Guid? TriggersSurfaceId { get; set; }
    public int ScenarioCount { get; set; }
    public long RealUserInteractionCount { get; set; }
}

public class E2EEntryPointDto
{
    public Guid Id { get; set; }
    public Guid TargetSurfaceId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OpenerKind { get; set; } = "toolbar_button";
    public string? ShortcutKeys { get; set; }
    public string Criticality { get; set; } = "p1";
}

public class E2EBlockTypeDto
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "text";
    public string? LatexRole { get; set; }
    public int ScenarioCount { get; set; }
}

// ---- Scenario reads ----

public class E2EScenarioListItemDto
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ModuleSlug { get; set; } = string.Empty;
    public string? SurfaceSlug { get; set; }
    public string? EntryPointSlug { get; set; }
    public string Criticality { get; set; } = "p1";
    public string DetailLevel { get; set; } = "l1";
    public string ReviewState { get; set; } = "draft";
    public string ExecutionMode { get; set; } = "integration";
    public string AutomationContent { get; set; } = string.Empty;
    public string? HealthState { get; set; }
    public decimal? FlakeScore { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class E2EScenarioDetailDto : E2EScenarioListItemDto
{
    public string? Description { get; set; }
    public int? EstimateSeconds { get; set; }
    public string? Milestone { get; set; }
    public string? ExploratoryMission { get; set; }
    public string? ExploratoryGoals { get; set; }
    public List<string> Tags { get; set; } = new();
    public E2EScenarioVersionDto? CurrentVersion { get; set; }
    public List<E2EScenarioVersionDto> Versions { get; set; } = new();
}

public class E2EScenarioVersionDto
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string DetailLevel { get; set; } = "l1";
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement? Preconditions { get; set; }
    public JsonElement Steps { get; set; }
    public JsonElement? ExpectedOutcomes { get; set; }
    public string GenerationProvenance { get; set; } = "human";
    public string? GeneratedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ---- Reporter writes ----

public class StartRunRequest
{
    public string TriggerKind { get; set; } = "manual";
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }
    public JsonElement? Environment { get; set; }
}

public class StartRunResponse
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
}

public class RecordResultRequest
{
    public Guid TestRunId { get; set; }
    /// <summary>Stable scenario fingerprint — links to scenario.automation_content.</summary>
    public string AutomationContent { get; set; } = string.Empty;
    public string DetailLevelRun { get; set; } = "l3";
    public string Outcome { get; set; } = "pass";
    public int DurationMs { get; set; }
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }
    public int? ViewportW { get; set; }
    public int? ViewportH { get; set; }
    public string? Locale { get; set; }
    public JsonElement? FeatureFlags { get; set; }
    public string? BackendBuildSha { get; set; }
    public int? TestOrderIndex { get; set; }
    public string? FailureKind { get; set; }
    public string? FailureMessage { get; set; }
    public string? FailureStack { get; set; }
    public int RetryAttempt { get; set; }
    public bool IsRetryPass { get; set; }
    public string? TracePath { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? VideoPath { get; set; }
    public JsonElement? Parametrisation { get; set; }
}

public class FinalizeRunRequest
{
    public string Status { get; set; } = "completed";
}

// ---- Coverage events ----

public class CoverageEventDto
{
    public string Source { get; set; } = "test";          // 'test' | 'real_user'
    public string? ScenarioResultId { get; set; }         // accepted as string, parsed to Guid
    public string? SurfaceSlug { get; set; }
    public string? UIElementSlug { get; set; }
    public string? ModuleSlug { get; set; }               // helps locate ambiguous surfaces
    public string InteractionKind { get; set; } = "click";
    public string? RealUserSessionHash { get; set; }
    public JsonElement? Metadata { get; set; }
}

public class CoverageEventsRequest
{
    public List<CoverageEventDto> Events { get; set; } = new();
}

// ---- Coverage reads ----

public class UIElementCoverageDto
{
    public Guid UIElementId { get; set; }
    public string UIElementSlug { get; set; } = string.Empty;
    public string SurfaceSlug { get; set; } = string.Empty;
    public int ScenarioCount { get; set; }
    public DateTime? LastTestExercise { get; set; }
    public DateTime? LastUserExercise { get; set; }
    public long RealUserInteractionCount { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsGhost => ScenarioCount == 0;
}
