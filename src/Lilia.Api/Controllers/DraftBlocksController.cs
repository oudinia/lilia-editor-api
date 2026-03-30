using System.Security.Claims;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/draft-blocks")]
[Authorize]
public class DraftBlocksController : ControllerBase
{
    private readonly IDraftBlockService _service;

    public DraftBlocksController(IDraftBlockService service)
    {
        _service = service;
    }

    private string GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? type = null,
        [FromQuery] string? category = null,
        [FromQuery] bool? favoritesOnly = null,
        [FromQuery] string? query = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var (items, total) = await _service.ListAsync(GetUserId(), type, category, favoritesOnly, query, page, pageSize);
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var dto = await _service.GetAsync(id, GetUserId());
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDraftBlockDto dto)
    {
        var result = await _service.CreateAsync(GetUserId(), dto);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDraftBlockDto dto)
    {
        var result = await _service.UpdateAsync(id, GetUserId(), dto);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _service.DeleteAsync(id, GetUserId());
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/favorite")]
    public async Task<IActionResult> ToggleFavorite(Guid id)
    {
        var ok = await _service.ToggleFavoriteAsync(id, GetUserId());
        return ok ? Ok() : NotFound();
    }

    [HttpPost("{id:guid}/commit")]
    public async Task<IActionResult> Commit(Guid id, [FromBody] CommitDraftBlockDto dto)
    {
        var blockId = await _service.CommitAsync(id, GetUserId(), dto);
        return blockId == null ? NotFound() : Ok(new { blockId });
    }

    [HttpPost("from-block")]
    public async Task<IActionResult> CreateFromBlock([FromBody] CreateDraftFromBlockDto dto)
    {
        var result = await _service.CreateFromBlockAsync(GetUserId(), dto);
        return result == null ? NotFound() : CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _service.GetCategoriesAsync(GetUserId());
        return Ok(categories);
    }
}
