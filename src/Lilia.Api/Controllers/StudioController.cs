using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/studio/{docId:guid}")]
[Authorize]
public class StudioController : ControllerBase
{
    private readonly IStudioService _studioService;
    private readonly IDocumentService _documentService;
    private readonly IVersionService _versionService;
    private readonly IPresenceService _presenceService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StudioController> _logger;

    public StudioController(
        IStudioService studioService,
        IDocumentService documentService,
        IVersionService versionService,
        IPresenceService presenceService,
        IServiceScopeFactory scopeFactory,
        ILogger<StudioController> logger)
    {
        _studioService = studioService;
        _documentService = documentService;
        _versionService = versionService;
        _presenceService = presenceService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    // --- Tree ---

    [HttpGet("tree")]
    public async Task<IActionResult> GetTree(Guid docId)
    {
        try
        {
            var tree = await _studioService.GetTreeAsync(docId);
            if (tree == null) return NotFound();
            return Ok(tree);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tree for document {DocumentId}", docId);
            return StatusCode(500, new { error = "Failed to load document tree", documentId = docId });
        }
    }

    // --- Block CRUD ---

    [HttpGet("block/{blockId:guid}")]
    public async Task<IActionResult> GetBlock(Guid docId, Guid blockId)
    {
        try
        {
            var block = await _studioService.GetBlockDetailAsync(docId, blockId);
            if (block == null) return NotFound();
            return Ok(block);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading block {BlockId} in document {DocumentId}", blockId, docId);
            return StatusCode(500, new { error = "Failed to load block", blockId, documentId = docId });
        }
    }

    [HttpPost("block")]
    public async Task<IActionResult> CreateBlock(Guid docId, [FromBody] CreateBlockDto dto)
    {
        var node = await _studioService.CreateBlockAsync(docId, dto);
        return Created($"api/studio/{docId}/block/{node.Id}", node);
    }

    [HttpPut("block/{blockId:guid}")]
    public async Task<IActionResult> UpdateBlock(Guid docId, Guid blockId, [FromBody] UpdateBlockDto dto)
    {
        var result = await _studioService.UpdateBlockContentAsync(docId, blockId, dto);
        if (result == null) return NotFound();

        // Auto-version (throttled — max 1 per 5 min per document). Must run
        // in its own DI scope — see BlocksController for the why.
        var userId = GetUserId();
        if (userId != null)
        {
            var logger = _logger;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var versionService = scope.ServiceProvider.GetRequiredService<IVersionService>();
                try { await versionService.CreateAutoVersionAsync(docId, userId); }
                catch (Exception ex) { logger.LogWarning(ex, "Auto-version failed for doc {DocId}", docId); }
            });
        }

        return Ok(result);
    }

    [HttpDelete("block/{blockId:guid}")]
    public async Task<IActionResult> DeleteBlock(Guid docId, Guid blockId)
    {
        var deleted = await _studioService.DeleteBlockAsync(docId, blockId);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPut("block/{blockId:guid}/move")]
    public async Task<IActionResult> MoveBlock(Guid docId, Guid blockId, [FromBody] MoveBlockDto dto)
    {
        var moved = await _studioService.MoveBlockAsync(docId, blockId, dto);
        if (!moved) return NotFound();
        return NoContent();
    }

    [HttpPatch("block/{blockId:guid}/metadata")]
    public async Task<IActionResult> UpdateBlockMetadata(Guid docId, Guid blockId, [FromBody] UpdateBlockMetadataDto dto)
    {
        var updated = await _studioService.UpdateBlockMetadataAsync(docId, blockId, dto);
        if (!updated) return NotFound();
        return NoContent();
    }

    // --- Preview ---

    [HttpGet("block/{blockId:guid}/preview")]
    public async Task<IActionResult> GetBlockPreview(Guid docId, Guid blockId, [FromQuery] string format = "html")
    {
        try
        {
            // Try cache first
            var cached = await _studioService.GetBlockPreviewAsync(blockId, format);
            if (cached?.Data != null)
            {
                return format switch
                {
                    "html" => Content(System.Text.Encoding.UTF8.GetString(cached.Data), "text/html"),
                    "latex" => Content(System.Text.Encoding.UTF8.GetString(cached.Data), "text/plain"),
                    _ => File(cached.Data, "application/octet-stream")
                };
            }

            // Render fresh
            var rendered = await _studioService.RenderBlockPreviewAsync(docId, blockId, format);
            if (rendered.Data == null)
                return NoContent();

            return format switch
            {
                "html" => Content(System.Text.Encoding.UTF8.GetString(rendered.Data), "text/html"),
                "latex" => Content(System.Text.Encoding.UTF8.GetString(rendered.Data), "text/plain"),
                _ => File(rendered.Data, "application/octet-stream")
            };
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering preview for block {BlockId} in document {DocumentId}", blockId, docId);
            return StatusCode(500, new { error = "Failed to render block preview", blockId, documentId = docId });
        }
    }

    [HttpPost("block/{blockId:guid}/preview")]
    public async Task<IActionResult> RenderBlockPreview(Guid docId, Guid blockId, [FromBody] RenderBlockPreviewDto dto)
    {
        var rendered = await _studioService.RenderBlockPreviewAsync(docId, blockId, dto.Format);
        return Ok(rendered);
    }

    /// <summary>
    /// Batch: return all cached block previews for a document in a single
    /// call. Kills the N+1 that bit StudioCardFlow — 235 blocks used to
    /// mean 235 requests on first load. This endpoint returns a dict
    /// keyed by blockId with the cached content per format, so the
    /// client pre-populates its in-memory map before card-level components
    /// mount. Cache-misses still fall back to the single-block GET.
    /// </summary>
    [HttpGet("blocks/previews")]
    public async Task<IActionResult> GetBlockPreviewsBatch(
        Guid docId,
        [FromQuery] string format = "latex")
    {
        var map = await _studioService.GetBlockPreviewsForDocumentAsync(docId, format);
        return Ok(map);
    }

    // --- Session ---

    [HttpGet("session")]
    public async Task<IActionResult> GetSession(Guid docId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var session = await _studioService.GetSessionAsync(userId, docId);
        if (session == null) return Ok(new { }); // Empty object for new sessions
        return Ok(session);
    }

    [HttpPut("session")]
    public async Task<IActionResult> SaveSession(Guid docId, [FromBody] SaveStudioSessionDto dto)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var session = await _studioService.SaveSessionAsync(userId, docId, dto);
        return Ok(session);
    }

    // --- Block Locks ---

    [HttpGet("locks")]
    public IActionResult GetBlockLocks(Guid docId)
    {
        var locks = _presenceService.GetLockedBlocks(docId.ToString());
        var result = locks.Select(kvp => new
        {
            blockId = kvp.Key,
            userId = kvp.Value.UserId,
            displayName = kvp.Value.DisplayName,
        });
        return Ok(new { locks = result });
    }
}
