using System.Text.Json;

namespace Lilia.Core.Entities.E2E;

// =====================================================================
//  E2E scenario database — Run-time layer.
//
//  Test runs, per-scenario results, UI interaction events (from both
//  tests and real users), nightly health rollup, and insight queue
//  for Claude-driven refinement.
// =====================================================================

public class E2ETestRun
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    /// <summary>ci | manual | scheduled | hosted</summary>
    public string TriggerKind { get; set; } = "manual";
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }
    public JsonDocument? Environment { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int? DurationMs { get; set; }
    public JsonDocument? ReporterMeta { get; set; }

    public virtual ICollection<E2EScenarioResult> Results { get; set; } = new List<E2EScenarioResult>();
}

public class E2EScenarioResult
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public Guid ScenarioId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    /// <summary>l1 | l2 | l3</summary>
    public string DetailLevelRun { get; set; } = "l3";

    /// <summary>pass | fail | skip | timed_out | interrupted</summary>
    public string Outcome { get; set; } = "pass";
    public int DurationMs { get; set; }

    // Rich environment per result (Q2/Q4 — for drift vs bug classifier).
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }
    public int? ViewportW { get; set; }
    public int? ViewportH { get; set; }
    public string? Locale { get; set; }
    public JsonDocument? FeatureFlags { get; set; }
    public string? BackendBuildSha { get; set; }
    public int? TestOrderIndex { get; set; }
    public bool? PriorSessionResidueDetected { get; set; }

    /// <summary>selector_not_found | timeout_navigation | timeout_visibility | assertion_failed | navigation_failed | infrastructure_error | visual_regression | console_error</summary>
    public string? FailureKind { get; set; }
    public string? FailureMessage { get; set; }
    public string? FailureStack { get; set; }

    public bool IsRetryPass { get; set; } = false;
    public Guid? RetryOfId { get; set; }
    public int RetryAttempt { get; set; } = 0;
    public JsonDocument? Parametrisation { get; set; }

    public string? TracePath { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? VideoPath { get; set; }
    public decimal? DurationZScore { get; set; }
    public bool? ScreenshotDiffIsolated { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public virtual E2ETestRun TestRun { get; set; } = null!;
    public virtual E2EScenario Scenario { get; set; } = null!;
    public virtual E2EScenarioVersion ScenarioVersion { get; set; } = null!;
    public virtual E2EScenarioResult? RetryOf { get; set; }
    public virtual ICollection<E2EUIInteractionEvent> InteractionEvents { get; set; } = new List<E2EUIInteractionEvent>();
}

public class E2EUIInteractionEvent
{
    public Guid Id { get; set; }
    /// <summary>test | real_user</summary>
    public string Source { get; set; } = "test";
    public Guid? ScenarioResultId { get; set; }
    /// <summary>Short non-PII hash (rotates daily) — never user_id.</summary>
    public string? RealUserSessionHash { get; set; }

    public Guid? UIElementId { get; set; }
    public Guid? SurfaceId { get; set; }

    /// <summary>click | dblclick | type | focus | select | navigate | submit | drag | hover | keypress</summary>
    public string InteractionKind { get; set; } = "click";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public JsonDocument? Metadata { get; set; }

    public virtual E2EScenarioResult? ScenarioResult { get; set; }
    public virtual E2EUIElement? UIElement { get; set; }
    public virtual E2ESurface? Surface { get; set; }
}

public class E2EScenarioHealthState
{
    public Guid ScenarioId { get; set; }
    public decimal FlakeScore { get; set; } = 0;
    /// <summary>green | watching | quarantined | investigated_as_bug</summary>
    public string HealthState { get; set; } = "green";
    public DateTime StateChangedAt { get; set; } = DateTime.UtcNow;
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    public virtual E2EScenario Scenario { get; set; } = null!;
}

public class E2EScenarioInsight
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    /// <summary>selector_drift | behavioral_drift | flake_cluster | coverage_gap | promotion_proposal | probable_product_bug | duplicate_scenario | ghost_element</summary>
    public string Kind { get; set; } = "selector_drift";
    public decimal Confidence { get; set; } = 0;
    public bool AutoApplyEligible { get; set; } = false;
    /// <summary>open | pending_review | applied | dismissed | rolled_back</summary>
    public string Status { get; set; } = "open";

    public JsonDocument? SuggestedChange { get; set; }
    public Guid[]? EvidenceRunIds { get; set; }
    public Guid? PriorVersionId { get; set; }

    public bool AppliedThenUncoveredBug { get; set; } = false;

    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "claude";
    public DateTime? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public DateTime? RolledBackAt { get; set; }
    public string? RolledBackBy { get; set; }

    public virtual E2EScenario Scenario { get; set; } = null!;
    public virtual E2EScenarioVersion? PriorVersion { get; set; }
}
