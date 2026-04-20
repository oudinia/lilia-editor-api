using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
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
    private readonly IBlockTypeService _blockTypeService;
    private readonly IRenderService _renderService;
    private readonly IVersionService _versionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILatexFragmentParser _latexFragmentParser;
    private readonly ILogger<BlocksController> _logger;

    public BlocksController(IBlockService blockService, IDocumentService documentService, IBlockTypeService blockTypeService, IRenderService renderService, IVersionService versionService, IServiceScopeFactory scopeFactory, ILatexFragmentParser latexFragmentParser, ILogger<BlocksController> logger)
    {
        _blockService = blockService;
        _documentService = documentService;
        _blockTypeService = blockTypeService;
        _renderService = renderService;
        _versionService = versionService;
        _scopeFactory = scopeFactory;
        _latexFragmentParser = latexFragmentParser;
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

        // Auto-version (throttled, fire-and-forget). Must run in its OWN DI
        // scope — the request scope disposes its LiliaDbContext as soon as
        // this method returns, so capturing _versionService here would hit
        // 'Npgsql: A command is already in progress' when the background
        // task runs after the connection was reset.
        var logger = _logger;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var versionService = scope.ServiceProvider.GetRequiredService<IVersionService>();
            try { await versionService.CreateAutoVersionAsync(docId, userId); }
            catch (Exception ex) { logger.LogWarning(ex, "Auto-version failed for doc {DocId}", docId); }
        });

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

    [HttpPut("{id:guid}/convert")]
    public async Task<ActionResult<BlockDto>> ConvertBlock(Guid docId, Guid id, [FromBody] ConvertBlockDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        if (!_blockTypeService.IsValidBlockType(dto.NewType))
            return BadRequest(new { message = $"Invalid block type: {dto.NewType}" });

        var block = await _blockService.ConvertBlockAsync(docId, id, dto.NewType);
        if (block == null) return NotFound();
        return Ok(block);
    }

    [HttpGet("{id:guid}/latex")]
    public async Task<ActionResult<object>> GetBlockLatex(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var blockDto = await _blockService.GetBlockAsync(docId, id);
        if (blockDto == null) return NotFound();

        // Create a temporary Block entity for rendering
        var block = new Block
        {
            Id = blockDto.Id,
            DocumentId = docId,
            Type = blockDto.Type,
            Content = System.Text.Json.JsonDocument.Parse(blockDto.Content.GetRawText()),
            SortOrder = blockDto.SortOrder
        };

        var latex = _renderService.RenderBlockToLatex(block);
        return Ok(new { latex });
    }

    /// <summary>
    /// v2 "edit block as LaTeX" round-trip (#68). Parses the posted fragment
    /// via ILatexFragmentParser, validates the top-level element matches the
    /// current block type, and updates the block content. Gated on
    /// <see cref="Document.ExperimentalLatexEdit"/> — 403 when the flag is off.
    /// </summary>
    [HttpPost("{id:guid}/from-latex")]
    public async Task<ActionResult<BlockDto>> UpdateBlockFromLatex(Guid docId, Guid id, [FromBody] FromLatexRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        if (!await _documentService.IsExperimentalLatexEditEnabledAsync(docId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "EXPERIMENTAL_LATEX_EDIT_DISABLED",
                message = "Enable \"Edit blocks as LaTeX (experimental)\" in document settings to use this surface.",
            });
        }

        var existing = await _blockService.GetBlockAsync(docId, id);
        if (existing == null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Latex))
        {
            return UnprocessableEntity(new
            {
                code = "EMPTY_FRAGMENT",
                diagnostics = new[] { new { severity = "error", code = "EMPTY_FRAGMENT", message = "LaTeX fragment is empty." } },
            });
        }

        try
        {
            using var content = await _latexFragmentParser.ParseFragmentAsync(request.Latex, existing.Type, ct);
            var updated = await _blockService.UpdateBlockAsync(docId, id, new UpdateBlockDto(
                Type: null,
                Content: content.RootElement.Clone(),
                SortOrder: null,
                ParentId: null,
                Depth: null));
            if (updated == null) return NotFound();
            return Ok(updated);
        }
        catch (LatexFragmentParseException ex)
        {
            return UnprocessableEntity(new
            {
                code = ex.Code,
                message = ex.Message,
                diagnostics = ex.Diagnostics,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure parsing LaTeX fragment for block {BlockId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                code = "INTERNAL",
                message = "Failed to parse LaTeX fragment.",
            });
        }
    }

    public sealed record FromLatexRequest(string Latex);

    /// <summary>
    /// Tier 1 bulk-convert: takes N adjacent blocks and folds them into a
    /// list / merged paragraph, or re-levels a run of headings. See
    /// <see cref="IBlockService.BatchConvertAsync"/> for semantics.
    /// Returns 400 when the action is unknown, 404 if any blockId doesn't
    /// belong to the document.
    /// </summary>
    [HttpPost("batch-convert")]
    public async Task<ActionResult<BatchConvertResultDto>> BatchConvert(Guid docId, [FromBody] BatchConvertBlocksDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        if (dto.BlockIds == null || dto.BlockIds.Count == 0)
        {
            return BadRequest(new { code = "EMPTY_SELECTION", message = "blockIds is required." });
        }

        var result = await _blockService.BatchConvertAsync(docId, dto);
        if (result == null)
        {
            return BadRequest(new
            {
                code = "INVALID_ACTION_OR_BLOCKS",
                message = "Unknown action, unknown blockId, or missing heading level for reheading.",
            });
        }
        return Ok(result);
    }
}
