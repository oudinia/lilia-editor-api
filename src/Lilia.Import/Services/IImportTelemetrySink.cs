namespace Lilia.Import.Services;

/// <summary>
/// One-shot record describing a parser-time event worth tracking.
/// Mirrors the columns of the <c>import_telemetry_events</c> table.
/// Defined inside Lilia.Import so parsers can populate it without
/// depending on Lilia.Core (the FK to ImportReviewSession is resolved
/// downstream when the sink writes).
/// </summary>
public sealed class ImportTelemetryRecord
{
    public string EventKind { get; set; } = string.Empty;
    public string Severity { get; set; } = "warn";
    public string SourceFormat { get; set; } = string.Empty;
    public string? TokenOrEnv { get; set; }
    public string? BlockKindEmitted { get; set; }
    public string? BlockKindExpected { get; set; }
    public string? SampleText { get; set; }
    public string? SourceFileName { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// Receives parser-time telemetry. Wired in DI: production registers
/// a DB-backed sink, tests register the no-op.
///
/// Sinks should be cheap on the hot path. The DB sink batches writes
/// internally so callers can fire-and-forget.
/// </summary>
public interface IImportTelemetrySink
{
    void Record(ImportTelemetryRecord evt);
}

/// <summary>Discards every event. Default for tests + when no DB.</summary>
public sealed class NoopImportTelemetrySink : IImportTelemetrySink
{
    public void Record(ImportTelemetryRecord evt) { /* no-op */ }
}
