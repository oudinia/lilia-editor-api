using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SnippetsController : ControllerBase
{
    private readonly ISnippetService _snippetService;

    public SnippetsController(ISnippetService snippetService)
    {
        _snippetService = snippetService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<SnippetPageDto>> GetSnippets([FromQuery] SnippetSearchDto search)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _snippetService.GetSnippetsAsync(userId, search);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnippetDto>> GetSnippet(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var snippet = await _snippetService.GetSnippetAsync(id, userId);
        if (snippet == null) return NotFound();
        return Ok(snippet);
    }

    [HttpPost]
    public async Task<ActionResult<SnippetDto>> CreateSnippet([FromBody] CreateSnippetDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var snippet = await _snippetService.CreateSnippetAsync(userId, dto);
        return CreatedAtAction(nameof(GetSnippet), new { id = snippet.Id }, snippet);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SnippetDto>> UpdateSnippet(Guid id, [FromBody] UpdateSnippetDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var snippet = await _snippetService.UpdateSnippetAsync(id, userId, dto);
        if (snippet == null) return NotFound();
        return Ok(snippet);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteSnippet(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _snippetService.DeleteSnippetAsync(id, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/favorite")]
    public async Task<ActionResult<SnippetDto>> ToggleFavorite(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var snippet = await _snippetService.ToggleFavoriteAsync(id, userId);
        if (snippet == null) return NotFound();
        return Ok(snippet);
    }

    [HttpPost("{id:guid}/use")]
    public async Task<ActionResult> UseSnippet(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _snippetService.IncrementUsageAsync(id, userId);
        if (!result) return NotFound();
        return Ok(new { success = true });
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
    {
        var categories = await _snippetService.GetCategoriesAsync();
        return Ok(categories);
    }
}
