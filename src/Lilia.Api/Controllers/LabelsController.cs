using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LabelsController : ControllerBase
{
    private readonly ILabelService _labelService;
    private readonly IDocumentService _documentService;

    public LabelsController(ILabelService labelService, IDocumentService documentService)
    {
        _labelService = labelService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<LabelDto>>> GetLabels()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var labels = await _labelService.GetLabelsAsync(userId);
        return Ok(labels);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LabelDto>> GetLabel(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var label = await _labelService.GetLabelAsync(userId, id);
        if (label == null) return NotFound();
        return Ok(label);
    }

    [HttpPost]
    public async Task<ActionResult<LabelDto>> CreateLabel([FromBody] CreateLabelDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var label = await _labelService.CreateLabelAsync(userId, dto);
        return CreatedAtAction(nameof(GetLabel), new { id = label.Id }, label);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LabelDto>> UpdateLabel(Guid id, [FromBody] UpdateLabelDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var label = await _labelService.UpdateLabelAsync(userId, id, dto);
        if (label == null) return NotFound();
        return Ok(label);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteLabel(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _labelService.DeleteLabelAsync(userId, id);
        if (!result) return NotFound();
        return NoContent();
    }
}

[ApiController]
[Route("api/documents/{docId:guid}/labels")]
[Authorize]
public class DocumentLabelsController : ControllerBase
{
    private readonly ILabelService _labelService;
    private readonly IDocumentService _documentService;

    public DocumentLabelsController(ILabelService labelService, IDocumentService documentService)
    {
        _labelService = labelService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpPost("{labelId:guid}")]
    public async Task<ActionResult> AddLabelToDocument(Guid docId, Guid labelId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _labelService.AddLabelToDocumentAsync(docId, labelId, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpDelete("{labelId:guid}")]
    public async Task<ActionResult> RemoveLabelFromDocument(Guid docId, Guid labelId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _labelService.RemoveLabelFromDocumentAsync(docId, labelId, userId);
        if (!result) return NotFound();
        return NoContent();
    }
}
