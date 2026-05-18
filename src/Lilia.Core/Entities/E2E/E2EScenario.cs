using System.Text.Json;

namespace Lilia.Core.Entities.E2E;

// =====================================================================
//  E2E scenario database — Scenario layer.
//
//  Scenarios + versioned snapshots + step rows + tag/coverage joins.
//  Each scenario has L1/L2/L3 depth; versions are immutable snapshots
//  so promotion (L1 → L2 → L3) creates new rows and result history
//  pins to whichever version ran.
// =====================================================================

public class E2EScenario
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid ModuleId { get; set; }
    public Guid? TargetSurfaceId { get; set; }
    public Guid? EntryPointId { get; set; }

    public string Criticality { get; set; } = "p1";      // p0 | p1 | p2
    public string DetailLevel { get; set; } = "l1";      // l1 | l2 | l3
    public string ReviewState { get; set; } = "draft";   // draft | approved | quarantined | deprecated
    public string ExecutionMode { get; set; } = "integration"; // integration | component
    public string Template { get; set; } = "standard";   // standard | exploratory | parametrised | accessibility

    // Q1 — stable fingerprint linking Playwright tests to scenarios.
    public string AutomationContent { get; set; } = string.Empty;
    public int? EstimateSeconds { get; set; }
    public int? EstimateForecastSeconds { get; set; }
    public string? Milestone { get; set; }

    // Exploratory-only.
    public string? ExploratoryMission { get; set; }
    public string? ExploratoryGoals { get; set; }

    // Pointer to the currently-active version (circular FK).
    public Guid? CurrentVersionId { get; set; }

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public virtual E2EModule Module { get; set; } = null!;
    public virtual E2ESurface? TargetSurface { get; set; }
    public virtual E2EEntryPoint? EntryPoint { get; set; }
    public virtual E2EScenarioVersion? CurrentVersion { get; set; }
    public virtual ICollection<E2EScenarioVersion> Versions { get; set; } = new List<E2EScenarioVersion>();
    public virtual ICollection<E2EScenarioTag> Tags { get; set; } = new List<E2EScenarioTag>();
    public virtual ICollection<E2EScenarioCoverageLink> CoverageLinks { get; set; } = new List<E2EScenarioCoverageLink>();
    public virtual ICollection<E2EScenarioResult> Results { get; set; } = new List<E2EScenarioResult>();
    public virtual ICollection<E2EScenarioInsight> Insights { get; set; } = new List<E2EScenarioInsight>();
}

public class E2EScenarioVersion
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>l1 | l2 | l3</summary>
    public string DetailLevel { get; set; } = "l1";
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonDocument? Preconditions { get; set; }
    public JsonDocument Steps { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument? ExpectedOutcomes { get; set; }
    /// <summary>human | llm_draft | llm_repaired | imported | session_recording</summary>
    public string GenerationProvenance { get; set; } = "human";
    public string? GeneratedBy { get; set; }
    public Guid? ParentVersionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2EScenario Scenario { get; set; } = null!;
    public virtual E2EScenarioVersion? ParentVersion { get; set; }
    public virtual ICollection<E2EScenarioStep> StepRows { get; set; } = new List<E2EScenarioStep>();
}

public class E2EScenarioStep
{
    public Guid Id { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public int SortOrder { get; set; }
    /// <summary>setup | action | wait | assert | teardown</summary>
    public string StepKind { get; set; } = "action";
    public string Description { get; set; } = string.Empty;
    public Guid? TargetUIElementId { get; set; }
    /// <summary>click | dblclick | type | press | select | check | uncheck | focus | blur | hover | drag | drop | navigate | wait_for | expect</summary>
    public string? ActionKind { get; set; }
    public JsonDocument? Payload { get; set; }
    public string? TechnicalAssertion { get; set; }
    public string? UserVisibleOutcome { get; set; }
    public Guid? SharedStepId { get; set; }
    public string? AdditionalInfo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2EScenarioVersion ScenarioVersion { get; set; } = null!;
    public virtual E2EUIElement? TargetUIElement { get; set; }
    public virtual E2EScenarioStep? SharedStep { get; set; }
}

public class E2ETag
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; }
}

public class E2EScenarioTag
{
    public Guid ScenarioId { get; set; }
    public Guid TagId { get; set; }

    public virtual E2EScenario Scenario { get; set; } = null!;
    public virtual E2ETag Tag { get; set; } = null!;
}

public class E2EScenarioCoverageLink
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    /// <summary>l1 | l2 | l3</summary>
    public string Layer { get; set; } = "l1";
    /// <summary>ui_element | block_type | module | surface | entry_point</summary>
    public string TargetKind { get; set; } = "ui_element";
    public Guid TargetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2EScenario Scenario { get; set; } = null!;
}
