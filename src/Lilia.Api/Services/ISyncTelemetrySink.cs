namespace Lilia.Api.Services;

/// <summary>
/// One Flow-sync observability event. Mirrors the columns of the
/// <c>sync_telemetry_events</c> table. See
/// architecture/2026-05-21-flow-editor-save-model.md.
/// </summary>
public sealed class SyncTelemetryRecord
{
    public string EventKind { get; set; } = string.Empty;
    public string Severity { get; set; } = "warn";
    public string Source { get; set; } = "server";
    public Guid? DocumentId { get; set; }
    public string? UserId { get; set; }
    public int? AttemptCount { get; set; }
    public int? DurationMs { get; set; }
    public string? Detail { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// Receives Flow-editor sync telemetry. Wired in DI: production
/// registers a DB-backed sink that batches writes off a Channel; tests
/// register the no-op. Sinks are cheap on the hot path — callers
/// fire-and-forget.
/// </summary>
public interface ISyncTelemetrySink
{
    void Record(SyncTelemetryRecord evt);
}

/// <summary>Discards every event. Default for tests + when no DB.</summary>
public sealed class NoopSyncTelemetrySink : ISyncTelemetrySink
{
    public void Record(SyncTelemetryRecord evt) { /* no-op */ }
}
