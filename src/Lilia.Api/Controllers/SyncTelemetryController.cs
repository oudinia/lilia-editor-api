using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Ingests Flow-editor sync telemetry from the client — offline spans,
/// retry exhaustion and push failures. Server-side version conflicts are
/// recorded directly by <c>BlocksController</c>. This is the structured
/// replacement for ad-hoc client logging of sync events.
/// See architecture/2026-05-21-flow-editor-save-model.md.
/// </summary>
[ApiController]
[Route("api/sync/telemetry")]
[Authorize]
public class SyncTelemetryController : ControllerBase
{
    private static readonly HashSet<string> AllowedKinds =
        new() { "conflict", "sync_error", "retry_exhausted", "offline_span" };
    private static readonly HashSet<string> AllowedSeverities =
        new() { "info", "warn", "error" };

    private readonly ISyncTelemetrySink _sink;

    public SyncTelemetryController(ISyncTelemetrySink sink)
    {
        _sink = sink;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpPost]
    public IActionResult Ingest([FromBody] SyncTelemetryBatchDto batch)
    {
        if (batch.Events == null || batch.Events.Count == 0)
            return Accepted();

        var userId = GetUserId();

        // Cap the batch so a misbehaving client can't flood the channel.
        foreach (var evt in batch.Events.Take(100))
        {
            var kind = evt.EventKind?.Trim() ?? string.Empty;
            if (!AllowedKinds.Contains(kind)) continue;

            _sink.Record(new SyncTelemetryRecord
            {
                EventKind = kind,
                Severity = AllowedSeverities.Contains(evt.Severity ?? string.Empty) ? evt.Severity! : "warn",
                Source = "client",
                DocumentId = evt.DocumentId,
                UserId = userId,
                AttemptCount = evt.AttemptCount,
                DurationMs = evt.DurationMs,
                Detail = evt.Detail,
                Metadata = evt.Metadata,
            });
        }

        return Accepted();
    }
}

public class SyncTelemetryBatchDto
{
    public List<SyncTelemetryEventDto> Events { get; set; } = [];
}

public class SyncTelemetryEventDto
{
    public string? EventKind { get; set; }
    public string? Severity { get; set; }
    public Guid? DocumentId { get; set; }
    public int? AttemptCount { get; set; }
    public int? DurationMs { get; set; }
    public string? Detail { get; set; }
    public string? Metadata { get; set; }
}
