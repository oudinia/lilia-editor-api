namespace Lilia.Core.Entities;

/// <summary>
/// Tracks silent fallbacks and coverage gaps across all import paths
/// (LaTeX, DOCX, ePub, PDF, LML, Overleaf zip). Distinct from
/// <see cref="ImportDiagnostic"/>: diagnostics are user-facing and
/// session-scoped (live until the review session is finalised);
/// telemetry is dev/ops-facing and persists with retention so we can
/// see drift in real-world content vs the test corpus.
///
/// Reference: SG-117 (b477ea1, 3384e81) — bare \begin{tabular} was
/// silently producing paragraph blocks for months. No error fired,
/// no test failed, but every preview showed raw LaTeX. This table
/// makes those silent fails first-class.
/// </summary>
public class ImportTelemetryEvent
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Closed vocabulary. Add new values via migration so analytics
    /// stay queryable.
    /// </summary>
    public string EventKind { get; set; } = string.Empty;

    /// <summary><c>info</c> | <c>warn</c> | <c>error</c>.</summary>
    public string Severity { get; set; } = "warn";

    /// <summary><c>latex</c> | <c>docx</c> | <c>epub</c> | <c>pdf</c> | <c>lml</c> | <c>overleaf-zip</c>.</summary>
    public string SourceFormat { get; set; } = string.Empty;

    /// <summary>The trigger — env name, command, token, etc. (e.g. <c>tabular</c>, <c>\itshape</c>).</summary>
    public string? TokenOrEnv { get; set; }

    /// <summary>What the parser actually emitted (e.g. <c>paragraph</c>).</summary>
    public string? BlockKindEmitted { get; set; }

    /// <summary>What it should have emitted, when known (e.g. <c>table</c>). Null when unknown.</summary>
    public string? BlockKindExpected { get; set; }

    public Guid? ImportSessionId { get; set; }
    public Guid? DocumentId { get; set; }
    public string? UserId { get; set; }

    /// <summary>≤200 char excerpt for context. May contain user content; PII-aware consumers must redact.</summary>
    public string? SampleText { get; set; }

    public string? SourceFileName { get; set; }

    /// <summary>Per-kind extras: line numbers, parser stage, options, etc. JSONB.</summary>
    public string? Metadata { get; set; }

    public virtual ImportReviewSession? Session { get; set; }
}
