using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Block-group endpoints (LILIA-136). Document-scoped CRUD on the
/// many-to-many block-grouping primitive. First user is the layout
/// dimension (multi-column regions); designed to host other dimensions
/// (review tags, counter scopes, style presets, source attribution)
/// without controller churn.
/// </summary>
[ApiController]
[Route("api/documents/{documentId:guid}/groups")]
[Authorize]
public class BlockGroupsController : ControllerBase
{
    private readonly IBlockGroupService _service;
    private readonly LiliaDbContext _context;
    private readonly ILogger<BlockGroupsController> _logger;

    public BlockGroupsController(
        IBlockGroupService service,
        LiliaDbContext context,
        ILogger<BlockGroupsController> logger)
    {
        _service = service;
        _context = context;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    private async Task<bool> UserOwnsDocument(Guid documentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        return await _context.Documents
            .AnyAsync(d => d.Id == documentId && d.OwnerId == userId);
    }

    /// <summary>List all groups attached to a document.</summary>
    [HttpGet]
    public async Task<ActionResult<List<BlockGroupDto>>> List(Guid documentId)
    {
        if (!await UserOwnsDocument(documentId)) return NotFound();
        var groups = await _service.GetGroupsAsync(documentId);
        return Ok(groups);
    }

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<BlockGroupDto>> Get(Guid documentId, Guid groupId)
    {
        if (!await UserOwnsDocument(documentId)) return NotFound();
        var group = await _service.GetGroupAsync(documentId, groupId);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpPost]
    public async Task<ActionResult<BlockGroupDto>> Create(Guid documentId, [FromBody] CreateBlockGroupDto dto)
    {
        if (!await UserOwnsDocument(documentId)) return NotFound();
        try
        {
            var group = await _service.CreateGroupAsync(documentId, dto);
            return group == null ? NotFound() : CreatedAtAction(nameof(Get), new { documentId, groupId = group.Id }, group);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // 409 Conflict — block(s) already in another group within the
            // same dimension. Caller is expected to delete or rebuild
            // the conflicting group explicitly.
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPatch("{groupId:guid}")]
    public async Task<ActionResult<BlockGroupDto>> Update(Guid documentId, Guid groupId, [FromBody] UpdateBlockGroupDto dto)
    {
        if (!await UserOwnsDocument(documentId)) return NotFound();
        try
        {
            var group = await _service.UpdateGroupAsync(documentId, groupId, dto);
            return group == null ? NotFound() : Ok(group);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("{groupId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, Guid groupId)
    {
        if (!await UserOwnsDocument(documentId)) return NotFound();
        var deleted = await _service.DeleteGroupAsync(documentId, groupId);
        return deleted ? NoContent() : NotFound();
    }
}
