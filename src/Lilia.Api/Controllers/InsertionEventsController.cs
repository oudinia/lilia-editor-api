using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Telemetry sink for the editor's insertion surfaces (InsertionsPanel,
/// ⌘K palette, slash menu, package-modal expansion). The editor batches
/// events client-side and POSTs them every few seconds; we bulk-insert
/// into <c>latex_insertion_events</c>.
///
/// Drives content prioritisation for ongoing <c>insert_template</c>
/// backfills — the most-clicked tokens with no curated template are
/// the next backlog. The /stats endpoint surfaces the report we'll
/// query weekly.
/// </summary>
[ApiController]
[Route("api/lilia/insertions")]
[Authorize]
public class InsertionEventsController : ControllerBase
{
    private readonly LiliaDbContext _db;
    private readonly ILogger<InsertionEventsController> _logger;

    public InsertionEventsController(LiliaDbContext db, ILogger<InsertionEventsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Batch-record insertion events. The editor flushes every ~3s so
    /// the typical request carries 1-5 events. Hard cap at 100 per
    /// request so a misbehaving client can't blow up the request body.
    /// </summary>
    [HttpPost("events")]
    public async Task<IActionResult> RecordEvents([FromBody] InsertionEventBatchDto batch, CancellationToken ct)
    {
        if (batch?.Events == null || batch.Events.Count == 0) return NoContent();
        if (batch.Events.Count > 100) return BadRequest(new { error = "Batch size capped at 100 events" });

        var userId = GetUserId();
        var rows = batch.Events.Select(e => new LatexInsertionEvent
        {
            TokenName = (e.Name ?? "").Trim(),
            TokenKind = (e.Kind ?? "command").Trim(),
            TokenPackageSlug = string.IsNullOrWhiteSpace(e.PackageSlug) ? null : e.PackageSlug.Trim(),
            Source = (e.Source ?? "panel").Trim(),
            UserId = userId,
            DocumentId = e.DocumentId,
            WrappedSelection = e.WrappedSelection,
            CreatedAt = DateTime.UtcNow,
        })
        .Where(r => !string.IsNullOrEmpty(r.TokenName)) // drop empty names defensively
        .ToList();

        if (rows.Count == 0) return NoContent();

        // Bulk insert via AddRange + SaveChanges. AddAsync is for cases
        // that need value generators async; SaveChangesAsync alone is
        // fine for ≤100 rows. (Memory: feedback_import_db_first.md says
        // avoid loop+Add for *bulk* operations — this is a single
        // SaveChanges call so we're good.)
        _db.LatexInsertionEvents.AddRange(rows);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[InsertionEvents] saved {Count} events for user {UserId}",
            rows.Count, userId);

        return NoContent();
    }

    /// <summary>
    /// Top tokens by insertion count over the recent window. Drives
    /// the weekly catalog backfill triage. Default window 30 days,
    /// configurable up to 365.
    /// </summary>
    [HttpGet("stats/top")]
    public async Task<ActionResult<List<InsertionStatRow>>> GetTopTokens(
        [FromQuery] int windowDays = 30,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        windowDays = Math.Clamp(windowDays, 1, 365);
        limit = Math.Clamp(limit, 1, 200);
        var since = DateTime.UtcNow.AddDays(-windowDays);

        var rows = await _db.LatexInsertionEvents.AsNoTracking()
            .Where(e => e.CreatedAt >= since)
            .GroupBy(e => new { e.TokenName, e.TokenKind, e.TokenPackageSlug })
            .Select(g => new InsertionStatRow(
                g.Key.TokenName,
                g.Key.TokenKind,
                g.Key.TokenPackageSlug,
                g.Count(),
                g.Select(x => x.UserId).Distinct().Count(),
                g.Count(x => x.WrappedSelection)
            ))
            .OrderByDescending(r => r.Hits)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Source-mix breakdown — which surface drives most insertions.
    /// Tells us where to invest UX polish.
    /// </summary>
    [HttpGet("stats/sources")]
    public async Task<ActionResult<List<InsertionSourceRow>>> GetSourceMix(
        [FromQuery] int windowDays = 30,
        CancellationToken ct = default)
    {
        windowDays = Math.Clamp(windowDays, 1, 365);
        var since = DateTime.UtcNow.AddDays(-windowDays);

        var rows = await _db.LatexInsertionEvents.AsNoTracking()
            .Where(e => e.CreatedAt >= since)
            .GroupBy(e => e.Source)
            .Select(g => new InsertionSourceRow(
                g.Key,
                g.Count(),
                g.Select(x => x.UserId).Distinct().Count()
            ))
            .OrderByDescending(r => r.Hits)
            .ToListAsync(ct);

        return Ok(rows);
    }
}

public sealed record InsertionEventDto(
    string Name,
    string Kind,
    string? PackageSlug,
    string Source,
    Guid? DocumentId,
    bool WrappedSelection);

public sealed record InsertionEventBatchDto(List<InsertionEventDto> Events);

public sealed record InsertionStatRow(
    string TokenName,
    string TokenKind,
    string? TokenPackageSlug,
    int Hits,
    int DistinctUsers,
    int WrappedSelectionHits);

public sealed record InsertionSourceRow(
    string Source,
    int Hits,
    int DistinctUsers);
