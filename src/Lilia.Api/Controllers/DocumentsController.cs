using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<DocumentListDto>>> GetDocuments(
        [FromQuery] string? search = null,
        [FromQuery] Guid? labelId = null)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var documents = await _documentService.GetDocumentsAsync(userId, search, labelId);
        return Ok(documents);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetDocument(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var document = await _documentService.GetDocumentAsync(id, userId);
        if (document == null) return NotFound();
        return Ok(document);
    }

    [HttpGet("shared/{shareLink}")]
    [AllowAnonymous]
    public async Task<ActionResult<DocumentDto>> GetSharedDocument(string shareLink)
    {
        var document = await _documentService.GetSharedDocumentAsync(shareLink);
        if (document == null) return NotFound();
        return Ok(document);
    }

    [HttpPost]
    public async Task<ActionResult<DocumentDto>> CreateDocument([FromBody] CreateDocumentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var document = await _documentService.CreateDocumentAsync(userId, dto);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> UpdateDocument(Guid id, [FromBody] UpdateDocumentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var document = await _documentService.UpdateDocumentAsync(id, userId, dto);
        if (document == null) return NotFound();
        return Ok(document);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteDocument(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _documentService.DeleteDocumentAsync(id, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<DocumentDto>> DuplicateDocument(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var document = await _documentService.DuplicateDocumentAsync(id, userId);
        if (document == null) return NotFound();
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
    }

    [HttpPost("{id:guid}/share")]
    public async Task<ActionResult<DocumentShareResultDto>> ShareDocument(Guid id, [FromBody] ShareDocumentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _documentService.ShareDocumentAsync(id, userId, dto.IsPublic);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:guid}/share")]
    public async Task<ActionResult> RevokeShare(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _documentService.RevokeShareAsync(id, userId);
        if (!result) return NotFound();
        return NoContent();
    }
}
