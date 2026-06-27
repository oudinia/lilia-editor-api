using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Public read of the "What's new / fixed" changelog, surfaced on /whats-new.
/// Anonymous (open) so a shared link renders for anyone. Localized fields are
/// returned in full; the client picks the reader's language.
/// </summary>
[ApiController]
[Route("api/public/changelog")]
[AllowAnonymous]
public class ChangelogController : ControllerBase
{
    private readonly LiliaDbContext _db;

    public ChangelogController(LiliaDbContext db) => _db = db;

    public record ChangelogEntryDto(
        Guid Id,
        string EntryDate,
        string Area,
        string Kind,
        string Status,
        Dictionary<string, string> Title,
        Dictionary<string, string> Detail,
        bool Verified,
        string? ShotUrl);

    /// <summary>GET /api/public/changelog — newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ChangelogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var entries = await _db.ChangelogEntries
            .AsNoTracking()
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.Sort)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => new ChangelogEntryDto(
                e.Id,
                e.EntryDate.ToString("yyyy-MM-dd"),
                e.Area,
                e.Kind,
                e.Status,
                e.Title,
                e.Detail,
                e.Verified,
                e.ShotUrl))
            .ToListAsync(ct);

        return Ok(entries);
    }
}
