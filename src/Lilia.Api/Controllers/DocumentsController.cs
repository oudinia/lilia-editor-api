using System.Diagnostics;
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
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentService documentService, ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Get paginated list of documents
    /// </summary>
    /// <param name="page">Page number (1-based, default: 1)</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100)</param>
    /// <param name="search">Search term for document title</param>
    /// <param name="labelId">Filter by label ID</param>
    /// <param name="sortBy">Sort field: title, createdAt, updatedAt (default: updatedAt)</param>
    /// <param name="sortDir">Sort direction: asc, desc (default: desc)</param>
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<DocumentListDto>>> GetDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? labelId = null,
        [FromQuery] string sortBy = "updatedAt",
        [FromQuery] string sortDir = "desc")
    {
        var sw = Stopwatch.StartNew();

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _documentService.GetDocumentsPaginatedAsync(
            userId, page, pageSize, search, labelId, sortBy, sortDir);

        _logger.LogInformation(
            "[Documents] GET list: total={TotalMs}ms, count={DocumentCount}/{TotalCount}, page={Page}/{TotalPages}, search={Search}",
            sw.ElapsedMilliseconds, result.Items.Count, result.TotalCount, result.Page, result.TotalPages, search);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetDocument(Guid id)
    {
        var sw = Stopwatch.StartNew();

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var authTime = sw.ElapsedMilliseconds;

        var document = await _documentService.GetDocumentAsync(id, userId);
        if (document == null) return NotFound();

        var queryTime = sw.ElapsedMilliseconds - authTime;

        _logger.LogInformation(
            "[Document] GET {DocumentId}: auth={AuthMs}ms, query={QueryMs}ms, total={TotalMs}ms, blocks={BlockCount}",
            id, authTime, queryTime, sw.ElapsedMilliseconds, document.Blocks?.Count ?? 0);

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
