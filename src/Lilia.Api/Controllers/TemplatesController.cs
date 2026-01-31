using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateService _templateService;

    public TemplatesController(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<TemplateListDto>>> GetTemplates([FromQuery] string? category = null)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var templates = await _templateService.GetTemplatesAsync(userId, category);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TemplateDto>> GetTemplate(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var template = await _templateService.GetTemplateAsync(id, userId);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost]
    public async Task<ActionResult<TemplateDto>> CreateTemplate([FromBody] CreateTemplateDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var template = await _templateService.CreateTemplateAsync(userId, dto);
        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TemplateDto>> UpdateTemplate(Guid id, [FromBody] UpdateTemplateDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var template = await _templateService.UpdateTemplateAsync(id, userId, dto);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteTemplate(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _templateService.DeleteTemplateAsync(id, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/use")]
    public async Task<ActionResult<DocumentDto>> UseTemplate(Guid id, [FromBody] UseTemplateDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var document = await _templateService.UseTemplateAsync(id, userId, dto);
        return Ok(document);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<TemplateCategoryDto>>> GetCategories()
    {
        var categories = await _templateService.GetCategoriesAsync();
        return Ok(categories);
    }
}
