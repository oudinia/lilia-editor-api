using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// An author's tables, owned independently of any document.
/// </summary>
/// <remarks>
/// A document uses a table by <b>reference</b>: one row, however many papers it
/// appears in, and an edit reaches all of them. Nothing here copies a table.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TablesController : ControllerBase
{
    private readonly LiliaDbContext _db;
    private readonly ILogger<TablesController> _logger;

    public TablesController(LiliaDbContext db, ILogger<TablesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>Tables this user owns or has been given access to.</summary>
    /// <remarks>
    /// Ordered by <c>updated_at</c>, which tables have and blocks never did — so
    /// "recently edited" is answered rather than approximated from a document.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TableSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        limit = Math.Clamp(limit, 1, 200);

        var rows = await _db.Tables
            .Where(t => t.OwnerId == userId || t.Collaborators.Any(c => c.UserId == userId))
            .OrderByDescending(t => t.UpdatedAt)
            .Skip(Math.Max(0, offset))
            .Take(limit)
            .Select(t => new
            {
                t.Id, t.Caption, t.Label, t.Content, t.CreatedAt, t.UpdatedAt, t.OwnerId,
                // How many papers use it. The listing shows this because it is
                // the one thing a table library has that a document list does not.
                DocumentCount = t.Documents.Count(),
            })
            .ToListAsync();

        return Ok(rows.Select(r => new TableSummaryDto(
            r.Id, r.Caption, r.Label, r.Content.RootElement.Clone(),
            r.CreatedAt, r.UpdatedAt, r.DocumentCount, r.OwnerId == userId)));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TableSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var t = await _db.Tables
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id &&
                (x.OwnerId == userId || x.Collaborators.Any(c => c.UserId == userId)));
        if (t is null) return NotFound();

        return Ok(new TableSummaryDto(t.Id, t.Caption, t.Label, t.Content.RootElement.Clone(),
            t.CreatedAt, t.UpdatedAt, t.Documents.Count, t.OwnerId == userId));
    }

    [HttpPost]
    [ProducesResponseType(typeof(TableSummaryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SaveTableDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var t = new TableEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            // Empty is normal and expected — the listing leads with a preview
            // because captions are frequently absent.
            Caption = dto.Caption ?? string.Empty,
            Label = dto.Label ?? string.Empty,
            Content = JsonDocument.Parse(dto.Content.GetRawText()),
        };
        _db.Tables.Add(t);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = t.Id },
            new TableSummaryDto(t.Id, t.Caption, t.Label, t.Content.RootElement.Clone(),
                t.CreatedAt, t.UpdatedAt, 0, true));
    }

    /// <summary>Update a table. Every document referencing it sees the change.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TableSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveTableDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var t = await _db.Tables.Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id &&
                (x.OwnerId == userId || x.Collaborators.Any(c => c.UserId == userId)));
        if (t is null) return NotFound();

        t.Caption = dto.Caption ?? string.Empty;
        t.Label = dto.Label ?? string.Empty;
        t.Content = JsonDocument.Parse(dto.Content.GetRawText());
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new TableSummaryDto(t.Id, t.Caption, t.Label, t.Content.RootElement.Clone(),
            t.CreatedAt, t.UpdatedAt, t.Documents.Count, t.OwnerId == userId));
    }

    /// <summary>Soft delete. Owner only — a collaborator can edit, not remove.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var t = await _db.Tables.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == userId);
        if (t is null) return NotFound();

        t.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("[Tables] {Id} soft-deleted by {User}", id, userId);
        return NoContent();
    }

    /// <summary>Which documents reference this table.</summary>
    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> Usage(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var exists = await _db.Tables.AnyAsync(x => x.Id == id &&
            (x.OwnerId == userId || x.Collaborators.Any(c => c.UserId == userId)));
        if (!exists) return NotFound();

        var rows = await _db.DocumentTables
            .Where(dt => dt.TableId == id)
            .Select(dt => new TableUsageDto(dt.DocumentId, dt.Document!.Title, dt.BlockId))
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>Attach this table to a document, by reference.</summary>
    [HttpPost("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> Attach(Guid id, Guid documentId, [FromQuery] Guid? blockId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var ownsTable = await _db.Tables.AnyAsync(x => x.Id == id &&
            (x.OwnerId == userId || x.Collaborators.Any(c => c.UserId == userId)));
        var ownsDoc = await _db.Documents.AnyAsync(d => d.Id == documentId && d.OwnerId == userId);
        if (!ownsTable || !ownsDoc) return NotFound();

        // Idempotent: attaching twice is the same as attaching once, and the
        // unique index says so too.
        var link = await _db.DocumentTables
            .FirstOrDefaultAsync(dt => dt.TableId == id && dt.DocumentId == documentId);
        if (link is null)
        {
            link = new DocumentTable { Id = Guid.NewGuid(), TableId = id, DocumentId = documentId, BlockId = blockId };
            _db.DocumentTables.Add(link);
        }
        else if (blockId.HasValue)
        {
            link.BlockId = blockId;
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Stop a document referencing this table. The table itself is untouched —
    /// detaching is not deleting, and it is not forking either.
    /// </summary>
    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> Detach(Guid id, Guid documentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var link = await _db.DocumentTables
            .FirstOrDefaultAsync(dt => dt.TableId == id && dt.DocumentId == documentId
                && (dt.Table!.OwnerId == userId || dt.Document!.OwnerId == userId));
        if (link is null) return NotFound();

        _db.DocumentTables.Remove(link);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

/// <summary>A table as the listing and the editor need it.</summary>
public record TableSummaryDto(
    Guid Id,
    string Caption,
    string Label,
    JsonElement Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    /// <summary>How many documents reference it. Zero is a normal state.</summary>
    int DocumentCount,
    bool IsOwner);

public record TableUsageDto(Guid DocumentId, string DocumentTitle, Guid? BlockId);

/// <summary>Create and update take the same body.</summary>
public record SaveTableDto(string? Caption, string? Label, JsonElement Content);
