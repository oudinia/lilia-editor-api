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

        var capture = new DiagnosticCapture
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RefToken = refToken,
            Source = string.IsNullOrWhiteSpace(dto.Source) ? "math-editor" : dto.Source,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim(),
            UserAgent = dto.UserAgent,
            Url = dto.Url,
            Payload = dto.Payload.GetRawText(),
            CreatedAt = DateTime.UtcNow,
        };
        _context.DiagnosticCaptures.Add(capture);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Diagnostic capture {RefToken} stored for user {UserId} (source={Source}, payloadBytes={Bytes})",
            refToken, userId ?? "(anon)", capture.Source, capture.Payload.Length);

        return new DiagnosticCaptureCreatedDto(capture.Id, capture.RefToken, capture.CreatedAt);
    }

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
