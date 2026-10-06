using System.Text.Json;
using System.Text.Json.Nodes;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines.Themes;
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
                t.Id, t.Caption, t.Label, t.Content, t.CreatedAt, t.UpdatedAt, t.OwnerId, t.CopiedFrom,
                // How many papers use it. The listing shows this because it is
                // the one thing a table library has that a document list does not.
                DocumentCount = t.Documents.Count(),
            })
            .ToListAsync();

        return Ok(rows.Select(r => new TableSummaryDto(
            r.Id, r.Caption, r.Label, r.Content.RootElement.Clone(),
            r.CreatedAt, r.UpdatedAt, r.DocumentCount, r.OwnerId == userId, r.CopiedFrom)));
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
            t.CreatedAt, t.UpdatedAt, t.Documents.Count, t.OwnerId == userId, t.CopiedFrom));
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
            CopiedFrom = dto.CopiedFrom,
        };
        _db.Tables.Add(t);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = t.Id },
            new TableSummaryDto(t.Id, t.Caption, t.Label, t.Content.RootElement.Clone(),
                t.CreatedAt, t.UpdatedAt, 0, true, t.CopiedFrom));
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
        await WriteThroughToLinkedBlocksAsync(t);
        await _db.SaveChangesAsync();

        return Ok(new TableSummaryDto(t.Id, t.Caption, t.Label, t.Content.RootElement.Clone(),
            t.CreatedAt, t.UpdatedAt, t.Documents.Count, t.OwnerId == userId, t.CopiedFrom));
    }

    /// <summary>
    /// A linked table's edit reaches the papers that use it: each linked block
    /// takes the table's grid, caption and label. Keys the block has and the
    /// table does not (a paper's own column widths, say) are kept.
    /// </summary>
    /// <remarks>
    /// The documents' versions move with the blocks, in the same SaveChanges, so
    /// an editor open on one of them sees a conflict and rebases, rather than
    /// its next save quietly putting the old table back (see ConcurrencyVersion).
    /// A copy is not linked here — it has its own row — so it is untouched.
    /// </remarks>
    private async Task WriteThroughToLinkedBlocksAsync(TableEntity t)
    {
        var links = t.Documents.Where(d => d.BlockId.HasValue)
            .Select(d => new { d.DocumentId, BlockId = d.BlockId!.Value })
            .ToList();
        if (links.Count == 0) return;

        var blockIds = links.Select(l => l.BlockId).ToList();
        var blocks = await _db.Blocks
            .Where(b => blockIds.Contains(b.Id) && b.Type == BlockTypes.Table)
            .ToListAsync();
        // The block must be in the document the link names — a block id alone
        // is not permission to write into whatever document holds it.
        blocks = blocks.Where(b => links.Any(l => l.BlockId == b.Id && l.DocumentId == b.DocumentId)).ToList();
        if (blocks.Count == 0) return;

        var table = JsonNode.Parse(t.Content.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
        var now = DateTime.UtcNow;
        foreach (var block in blocks)
        {
            var content = JsonNode.Parse(block.Content.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            foreach (var (key, value) in table)
                content[key] = value?.DeepClone();
            content["caption"] = t.Caption;
            content["label"] = t.Label;
            block.Content = JsonDocument.Parse(content.ToJsonString());
            block.UpdatedAt = now;
        }
        foreach (var documentId in blocks.Select(b => b.DocumentId).Distinct())
            await ConcurrencyVersion.BumpAsync(_db, documentId);
        _logger.LogInformation("[Tables] {Id} edit written through to {Count} linked block(s)", t.Id, blocks.Count);
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

    /// <summary>The tables this user deleted, newest deletion first. Owner only.</summary>
    /// <remarks>
    /// Tables have their own Trash: a deleted table is hidden by the global query
    /// filter, so this is the one place it can be seen again. A literal segment,
    /// so it never competes with the <c>{id:guid}</c> routes.
    /// </remarks>
    [HttpGet("trash")]
    [ProducesResponseType(typeof(IEnumerable<TrashedTableDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Trash()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // IgnoreQueryFilters reaches the navigation too: the link rows are
        // filtered by their table's DeletedAt, so without it every trashed table
        // would say it is used in no papers.
        var rows = await _db.Tables.IgnoreQueryFilters()
            .Where(t => t.OwnerId == userId && t.DeletedAt != null)
            .OrderByDescending(t => t.DeletedAt)
            .Select(t => new TrashedTableDto(
                t.Id, t.Caption, t.Label, t.DeletedAt!.Value, t.UpdatedAt,
                // Same count as the list: the papers using it.
                t.Documents.Count()))
            .ToListAsync();

        return Ok(rows);
    }

    /// <summary>Take a table out of the Trash. Owner only.</summary>
    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var t = await FindTrashedAsync(id, userId);
        if (t is null) return NotFound();

        // Its links were never removed, only hidden; they come back with it.
        t.DeletedAt = null;
        await _db.SaveChangesAsync();
        _logger.LogInformation("[Tables] {Id} restored from trash by {User}", id, userId);
        return NoContent();
    }

    /// <summary>Delete a trashed table for good. Owner only.</summary>
    /// <remarks>
    /// Only a table already in the Trash: a live one answers 404, the same as a
    /// table that does not exist or is someone else's — "not in your Trash" —
    /// rather than 409, so the endpoint never says what it will not act on.
    ///
    /// The database cascades the table's link rows (document_tables) and its
    /// collaborators (table_collaborators). Papers are not changed: a block keeps
    /// its own content, it just stops being linked. <c>copied_from</c> is a plain
    /// column, not a foreign key, so copies keep the id of the table they came
    /// from — provenance, not a reference — and nothing blocks the delete.
    /// </remarks>
    [HttpDelete("{id:guid}/permanent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purge(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var t = await FindTrashedAsync(id, userId);
        if (t is null) return NotFound();

        _db.Tables.Remove(t);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[Tables] {Id} permanently deleted by {User}", id, userId);
        return NoContent();
    }

    private Task<TableEntity?> FindTrashedAsync(Guid id, string userId) =>
        _db.Tables.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == userId && x.DeletedAt != null);

    /// <summary>Which documents reference this table.</summary>
    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> Usage(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var exists = await _db.Tables.AnyAsync(x => x.Id == id &&
            (x.OwnerId == userId || x.Collaborators.Any(c => c.UserId == userId)));
        if (!exists) return NotFound();

        // Permission-filtered. A table can be shared with someone who has no
        // access to the papers it is used in, and "used in 3 documents" must not
        // become a list of their titles. Olivia caught this in review.
        //
        // The count is still honest — you are told the table is used elsewhere,
        // just not by whom or in what. Hiding the number instead would make a
        // shared table look unused and invite someone to delete it.
        var links = await _db.DocumentTables
            .Where(dt => dt.TableId == id)
            .Select(dt => new
            {
                dt.DocumentId,
                dt.BlockId,
                Title = dt.Document!.Title,
                Visible = dt.Document!.OwnerId == userId
                          || dt.Document!.Collaborators.Any(c => c.UserId == userId),
            })
            .ToListAsync();

        // Copies are separate tables, so they are counted, never listed: "1 copy
        // was made from this table" (Olivia, tables-modern 1e). A number only,
        // like the papers the caller cannot see.
        var copies = await _db.Tables.CountAsync(x => x.CopiedFrom == id);

        // Each visible paper's look and the section the table sits in (design 2b), worked out
        // here from that paper's headings, never stored. Only papers the caller may read.
        var visible = links.Where(l => l.Visible).ToList();
        var places = await SectionPlacesAsync(visible.Select(l => (l.DocumentId, l.BlockId)).ToList());

        return Ok(new TableUsageResponse(
            links.Count,
            visible
                 .Select(l =>
                 {
                     var (look, place) = places.TryGetValue((l.DocumentId, l.BlockId), out var p) ? p : (null, SectionPlace.None);
                     return new TableUsageDto(l.DocumentId, l.Title, l.BlockId,
                         DocumentService.ReadLook(look), place.Number, place.Colour);
                 })
                 .ToList(),
            copies));
    }

    /// <summary>
    /// For each (document, table block): the document's stored look and the block's place among
    /// its top numbered headings (<see cref="ThemeSections"/>), counted over the body the export
    /// prints. Two queries per call, whatever the number of papers: every block's type and order,
    /// and the content of the blocks that can number (headings and raw LaTeX embeds).
    /// </summary>
    private async Task<Dictionary<(Guid DocumentId, Guid? BlockId), (string? Look, SectionPlace Place)>> SectionPlacesAsync(
        List<(Guid DocumentId, Guid? BlockId)> links)
    {
        var result = new Dictionary<(Guid, Guid?), (string?, SectionPlace)>();
        if (links.Count == 0) return result;
        var docIds = links.Select(l => l.DocumentId).Distinct().ToList();

        var docs = await _db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Title, d.Look, d.LatexDocumentClass })
            .ToDictionaryAsync(d => d.Id);
        var outline = await _db.Blocks.AsNoTracking()
            .Where(b => docIds.Contains(b.DocumentId))
            .Select(b => new { b.Id, b.DocumentId, b.Type, b.SortOrder })
            .ToListAsync();
        var contents = await _db.Blocks.AsNoTracking()
            .Where(b => docIds.Contains(b.DocumentId)
                        && (b.Type == "heading" || b.Type == "header" || b.Type == "embed"))
            .Select(b => new { b.Id, b.Content })
            .ToDictionaryAsync(b => b.Id, b => b.Content);

        foreach (var group in outline.GroupBy(b => b.DocumentId))
        {
            if (!docs.TryGetValue(group.Key, out var doc)) continue;
            var blocks = group.OrderBy(b => b.SortOrder).Select(b => new Block
            {
                Id = b.Id, DocumentId = b.DocumentId, Type = b.Type, SortOrder = b.SortOrder,
                Content = contents.TryGetValue(b.Id, out var c) ? c : JsonDocument.Parse("{}"),
            });
            var body = LaTeXExportService.BodyBlocks(doc.Title, blocks);
            var placesInDoc = ThemeSections.Places(body, doc.LatexDocumentClass, doc.Look);
            foreach (var link in links.Where(l => l.DocumentId == group.Key))
            {
                var place = link.BlockId is { } blockId && placesInDoc.TryGetValue(blockId, out var p) ? p : SectionPlace.None;
                result[link] = (doc.Look, place);
            }
        }
        foreach (var link in links.Where(l => !result.ContainsKey(l)))
            result[link] = (docs.TryGetValue(link.DocumentId, out var doc) ? doc.Look : null, SectionPlace.None);
        return result;
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
    bool IsOwner,
    /// <summary>The table this one was copied from, when it is a copy. The copy
    /// remembers where it came from; the original counts its copies.</summary>
    Guid? CopiedFrom = null);

/// <summary>A table in its owner's Trash.</summary>
public record TrashedTableDto(
    Guid Id,
    string Caption,
    string Label,
    DateTime DeletedAt,
    DateTime UpdatedAt,
    /// <summary>How many papers still link it; they keep their own copy of the content.</summary>
    int DocumentCount);

/// <summary>One paper a table is used in.</summary>
/// <param name="Look">That paper's look, the same shape as on the document; null is Classic.</param>
/// <param name="SectionNumber">The number of the top numbered heading the table block sits under
/// (the chapter in report and book, the section in article; an appendix's letter, "A"), as the PDF
/// prints it; null before the first one, under an unnumbered one, or without a block.</param>
/// <param name="SectionColour">That heading's Index colour, <c>#RRGGBB</c> (pins and the appendix
/// restart applied, as lilia-theme.sty computes it); null unless the paper's theme is Index.</param>
public record TableUsageDto(
    Guid DocumentId,
    string DocumentTitle,
    Guid? BlockId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    JsonElement? Look = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    string? SectionNumber = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    string? SectionColour = null);

/// <summary>
/// Where a table is used. <paramref name="Total"/> counts every document;
/// <paramref name="Visible"/> holds only the ones this caller may see;
/// <paramref name="Copies"/> counts the tables copied from this one.
/// </summary>
/// <remarks>
/// The two differ when a table is shared more widely than the papers using it.
/// The share sheet should say so — a recipient sees the table and the count, not
/// the titles.
/// </remarks>
public record TableUsageResponse(int Total, IReadOnlyList<TableUsageDto> Visible, int Copies = 0);

/// <summary>Create and update take the same body.</summary>
/// <param name="CopiedFrom">
/// Set when this table was made by detaching a copy from another. Provenance is
/// the point: a reference model dies of forking invisibly, so a fork that
/// records where it came from is a fork you can still reason about.
/// </param>
public record SaveTableDto(string? Caption, string? Label, JsonElement Content, Guid? CopiedFrom = null);
