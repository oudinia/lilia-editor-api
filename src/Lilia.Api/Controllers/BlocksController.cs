using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/[controller]")]
[Authorize]
public class BlocksController : ControllerBase
{
    private readonly IBlockService _blockService;
    private readonly IDocumentService _documentService;
    private readonly ILogger<BlocksController> _logger;

    public BlocksController(IBlockService blockService, IDocumentService documentService, ILogger<BlocksController> logger)
    {
        _blockService = blockService;
        _documentService = documentService;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<BlockDto>>> GetBlocks(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var blocks = await _blockService.GetBlocksAsync(docId);
        return Ok(blocks);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BlockDto>> GetBlock(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var block = await _blockService.GetBlockAsync(docId, id);
        if (block == null) return NotFound();
        return Ok(block);
    }

    [HttpPost]
    public async Task<ActionResult<BlockDto>> CreateBlock(Guid docId, [FromBody] CreateBlockDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var block = await _blockService.CreateBlockAsync(docId, dto);
        return CreatedAtAction(nameof(GetBlock), new { docId, id = block.Id }, block);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BlockDto>> UpdateBlock(Guid docId, Guid id, [FromBody] UpdateBlockDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var block = await _blockService.UpdateBlockAsync(docId, id, dto);
        if (block == null) return NotFound();
        return Ok(block);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteBlock(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _blockService.DeleteBlockAsync(docId, id);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("batch")]
    public async Task<ActionResult<List<BlockDto>>> BatchUpdateBlocks(Guid docId, [FromBody] BatchUpdateBlocksDto dto)
    {
        _logger.LogInformation("BatchUpdateBlocks called for doc {DocId} with {BlockCount} blocks", docId, dto.Blocks?.Count ?? 0);
        if (dto.Blocks != null && dto.Blocks.Count > 0)
        {
            foreach (var block in dto.Blocks.Take(3))
            {
                _logger.LogDebug("Block {BlockId}: Type={Type}, HasContent={HasContent}",
                    block.Id, block.Type, block.Content.HasValue);
            }
        }

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var blocks = await _blockService.BatchUpdateBlocksAsync(docId, dto.Blocks);
        return Ok(blocks);
    }

    [HttpPut("reorder")]
    public async Task<ActionResult<List<BlockDto>>> ReorderBlocks(Guid docId, [FromBody] ReorderBlocksDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var blocks = await _blockService.ReorderBlocksAsync(docId, dto.BlockIds);
        return Ok(blocks);
    }
}
