namespace Lilia.Core.Entities;

/// <summary>
/// Observability for the Flow editor's continuous background sync.
/// Captures the events that otherwise fail silently — version
/// conflicts, push failures, retry exhaustion and offline spans.
/// Server-side conflicts are recorded by <c>BlocksController</c>;
/// client-side events (offline spans, retry exhaustion) arrive via
/// <c>POST /api/sync/telemetry</c>. Dev/ops-facing, persisted with
/// retention so we can see sync health drift in the real world.
/// Reference: architecture/2026-05-21-flow-editor-save-model.md.
/// </summary>
public class SyncTelemetryEvent
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Closed vocabulary: <c>conflict</c> | <c>sync_error</c> |
    /// <c>retry_exhausted</c> | <c>offline_span</c>. Add new values via
    /// migration so the analytics surface stays queryable.
    /// </summary>
    public string EventKind { get; set; } = string.Empty;

    /// <summary><c>info</c> | <c>warn</c> | <c>error</c>.</summary>
    public string Severity { get; set; } = "warn";

    /// <summary><c>server</c> | <c>client</c> — where the event was observed.</summary>
    public string Source { get; set; } = "server";

    public Guid? DocumentId { get; set; }
    public string? UserId { get; set; }

    /// <summary>Attempts made before the event fired (<c>retry_exhausted</c>).</summary>
    public int? AttemptCount { get; set; }

    /// <summary>Span length in milliseconds (<c>offline_span</c>).</summary>
    public int? DurationMs { get; set; }

    /// <summary>≤500-char free-text detail or error message.</summary>
    public string? Detail { get; set; }

    /// <summary>Per-kind extras as JSON.</summary>
    public string? Metadata { get; set; }
}
