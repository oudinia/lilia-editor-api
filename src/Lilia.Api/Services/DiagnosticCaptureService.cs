using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class DiagnosticCaptureService : IDiagnosticCaptureService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<DiagnosticCaptureService> _logger;

    public DiagnosticCaptureService(LiliaDbContext context, ILogger<DiagnosticCaptureService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DiagnosticCaptureCreatedDto> CreateAsync(string? userId, CreateDiagnosticCaptureDto dto)
    {
        var refToken = GenerateRefToken();
        // The rare slug collision (~1 in 2^40) retries with a new
        // token rather than surfacing a stack trace.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var clash = await _context.DiagnosticCaptures
                .AsNoTracking()
                .AnyAsync(c => c.RefToken == refToken);
            if (!clash) break;
            refToken = GenerateRefToken();
        }

        // Enrich the payload with recent sync_telemetry events for the
        // same user (optionally scoped to the capture's document). The
        // analyst gets a self-contained snapshot — no second query
        // required — and the join survives the 30-day retention sweep
        // because the linked rows live inside the capture's jsonb.
        var enrichedPayload = await EnrichWithSyncEventsAsync(dto.Payload, userId);

        var capture = new DiagnosticCapture
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RefToken = refToken,
            Source = string.IsNullOrWhiteSpace(dto.Source) ? "math-editor" : dto.Source,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim(),
            UserAgent = dto.UserAgent,
            Url = dto.Url,
            Payload = enrichedPayload,
            CreatedAt = DateTime.UtcNow,
        };
        _context.DiagnosticCaptures.Add(capture);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Diagnostic capture {RefToken} stored for user {UserId} (source={Source}, payloadBytes={Bytes})",
            refToken, userId ?? "(anon)", capture.Source, capture.Payload.Length);

        return new DiagnosticCaptureCreatedDto(capture.Id, capture.RefToken, capture.CreatedAt);
    }

    /// <summary>
    /// Look up sync_telemetry rows that probably correlate with this
    /// capture and stitch them into the payload under
    /// <c>payload.linkedSyncEvents</c>. The match window is
    /// (user_id, optional document_id, last <see cref="SyncLookbackMinutes"/>
    /// minutes). When the client's payload doesn't expose a docId we
    /// fall back to user-only — broader but still bounded by the
    /// time window.
    /// </summary>
    private async Task<string> EnrichWithSyncEventsAsync(JsonElement payload, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return payload.GetRawText();
        try
        {
            // Pull docId out of the payload if the client included it.
            Guid? docId = null;
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("document", out var doc) &&
                doc.ValueKind == JsonValueKind.Object &&
                doc.TryGetProperty("docId", out var docIdProp) &&
                docIdProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(docIdProp.GetString(), out var parsedDocId))
            {
                docId = parsedDocId;
            }

            var since = DateTime.UtcNow.AddMinutes(-SyncLookbackMinutes);
            var query = _context.SyncTelemetryEvents
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.CreatedAt >= since);
            if (docId is Guid d) query = query.Where(e => e.DocumentId == d);

            var matches = await query
                .OrderByDescending(e => e.CreatedAt)
                .Take(SyncLinkLimit)
                .Select(e => new
                {
                    id = e.Id,
                    eventKind = e.EventKind,
                    severity = e.Severity,
                    source = e.Source,
                    createdAt = e.CreatedAt,
                    detail = e.Detail,
                    attemptCount = e.AttemptCount,
                    durationMs = e.DurationMs,
                })
                .ToListAsync();

            // Splice into the existing JSON. We rebuild the root
            // object so a top-level reserved key doesn't accidentally
            // get overwritten if the client also sent one.
            using var input = JsonDocument.Parse(payload.GetRawText());
            using var ms = new MemoryStream();
            await using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            foreach (var prop in input.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("linkedSyncEvents")) continue; // we own this key
                prop.WriteTo(writer);
            }
            writer.WritePropertyName("linkedSyncEvents");
            JsonSerializer.Serialize(writer, matches);
            writer.WriteEndObject();
            await writer.FlushAsync();
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sync-event enrichment failed — storing payload as-is");
            return payload.GetRawText();
        }
    }

    /// <summary>
    /// Window (minutes) to look back for sync events when enriching a
    /// capture. 10 covers the typical 'I hit save 3 times in a minute'
    /// → 409 → rebase → eventual recovery; a longer window starts
    /// bringing in noise from prior debug sessions.
    /// </summary>
    private const int SyncLookbackMinutes = 10;
    /// <summary>Hard cap on linked events to keep the payload small.</summary>
    private const int SyncLinkLimit = 50;

    public async Task<DiagnosticCaptureDto?> GetByRefTokenAsync(string refToken, string? requesterUserId, bool isAdmin)
    {
        var capture = await _context.DiagnosticCaptures
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RefToken == refToken);
        if (capture is null) return null;

        // Visibility: owner can always read; admin can read anything;
        // everyone else gets null (treat as not-found to avoid leaking
        // existence of arbitrary tokens).
        if (!isAdmin && capture.UserId != requesterUserId) return null;

        return ToDto(capture);
    }

    public async Task<List<DiagnosticCaptureDto>> ListMineAsync(string userId, int limit = 20)
    {
        var items = await _context.DiagnosticCaptures
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync();
        return items.Select(ToDto).ToList();
    }

    private static DiagnosticCaptureDto ToDto(DiagnosticCapture c)
    {
        // Re-parse the stored jsonb so the wire shape matches the
        // raw bundle the client sent. If the column is somehow
        // empty/invalid, fall back to an empty object — callers
        // can still surface metadata.
        var payload = TryParse(c.Payload);
        return new DiagnosticCaptureDto(
            c.Id, c.UserId, c.RefToken, c.Source, c.Note,
            c.UserAgent, c.Url, payload, c.CreatedAt);
    }

    private static JsonElement TryParse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            using var fallback = JsonDocument.Parse("{}");
            return fallback.RootElement.Clone();
        }
    }

    /// <summary>
    /// Short, URL-safe, human-readable identifier — <c>cap-7a3b2c9f</c>.
    /// 8 hex chars ≈ 32 bits of entropy, which is plenty for the
    /// "minutes-to-days" debug share window.
    /// </summary>
    private static string GenerateRefToken()
    {
        var guid = Guid.NewGuid().ToByteArray();
        var hex = Convert.ToHexString(guid, 0, 4).ToLowerInvariant();
        return $"cap-{hex}";
    }
}
