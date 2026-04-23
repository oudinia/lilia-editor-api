using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Lightweight per-instance summary retained after the retention sweep
/// deletes the instance + rev_* rows (FT-IMP-001 stage 9). Privacy-safe
/// by design — stores analytics signals only, no raw content.
///
/// Populated by the daily retention job before it cascades. Kept
/// indefinitely (no retention cap today; revisit when the table grows
/// past ~1 GB).
/// </summary>
public class ImportArchiveStats
{
    public Guid Id { get; set; }

    /// <summary>
    /// Original instance id — the row is gone from
    /// <see cref="ImportReviewSession"/>, but we keep the identifier so
    /// future correlations with other audit tables still work.
    /// </summary>
    public Guid InstanceId { get; set; }

    public Guid? DefinitionId { get; set; }
    public string OwnerId { get; set; } = string.Empty;

    public string? SourceFormat { get; set; }
    public string? DocumentClass { get; set; }

    /// <summary>
    /// Final state before archive — `imported`, `cancelled`, `superseded`,
    /// or `abandoned` (the job's synthetic state for instances purged on
    /// UpdatedAt idleness without ever reaching a terminal status).
    /// </summary>
    public string FinalState { get; set; } = "unknown";

    public int TotalBlocks { get; set; }

    /// <summary>
    /// Block-type counts as jsonb — {paragraph: 126, heading: 14, ...}.
    /// Same shape as <see cref="Lilia.Core.DTOs.SessionSummaryDto.BlockCountsByType"/>.
    /// </summary>
    public JsonDocument? BlockCountsByType { get; set; }

    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int? QualityScore { get; set; }
    public double? CoverageMappedPercent { get; set; }
    public int UnsupportedTokenCount { get; set; }

    public DateTime InstanceCreatedAt { get; set; }
    public DateTime InstanceLastActivityAt { get; set; }
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Minutes from instance creation to terminal / abandon.
    /// </summary>
    public double? LifetimeMinutes { get; set; }
}
