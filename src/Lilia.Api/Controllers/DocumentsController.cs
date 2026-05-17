using System.Diagnostics;
using Lilia.Api.Models.Documents;
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
    private readonly IAuditService _auditService;

    public DocumentsController(IDocumentService documentService, ILogger<DocumentsController> logger, IAuditService auditService)
    {
        _documentService = documentService;
        _logger = logger;
        _auditService = auditService;
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

    [HttpGet("shared/{*shareLink}")]
    [AllowAnonymous]
    public async Task<ActionResult<DocumentDto>> GetSharedDocument(string shareLink)
    {
        // URL format: {slug}-{token} or just {token}
        // Token is always 22 chars (base64url of 16 bytes)
        var token = shareLink.Length > 22 ? shareLink[^22..] : shareLink;
        var document = await _documentService.GetSharedDocumentAsync(token);
        if (document == null) return NotFound();
        return Ok(document);
    }

    /// <summary>
    /// Spec §8 — "Make a copy" CTA from the public viewer chrome.
    /// Anonymous visitors get 401 so the SPA can redirect them to
    /// sign-up. Authenticated visitors clone the shared doc into
    /// their own library and we return the new doc.
    /// </summary>
    [HttpPost("shared/{*shareLink}/copy")]
    public async Task<ActionResult<DocumentDto>> CopySharedDocument(string shareLink)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Same slug-or-token parse as the GET path so callers can
        // pass either shape from the URL bar.
        var token = shareLink.Length > 22 ? shareLink[^22..] : shareLink;
        var copy = await _documentService.CloneSharedDocumentAsync(token, userId);
        if (copy == null) return NotFound();
        await _auditService.LogAsync("document.public.copy", "Document", copy.Id.ToString());
        return Ok(copy);
    }

    [HttpPost]
    public async Task<ActionResult<DocumentDto>> CreateDocument([FromBody] CreateDocumentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Truncate overly long titles to prevent DB overflow.
        if (dto.Title?.Length > 255)
            dto.Title = dto.Title[..255];

        var document = await _documentService.CreateDocumentAsync(userId, dto);
        await _auditService.LogAsync("document.create", "Document", document.Id.ToString(),
            new { dto.Title, dto.TeamId, dto.DocumentClass, dto.DocumentCategory });
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
        await _auditService.LogAsync("document.delete", "Document", id.ToString());
        return NoContent();
    }

    /// <summary>
    /// Attach or detach a document to/from a team. The plain
    /// PUT /documents/{id} flow used to "support" teamId via the
    /// UpdateDocumentDto body, but the DTO never carried the
    /// field — so every previous attempt silently no-op'd. This
    /// dedicated endpoint matches the spec vocabulary (Attach /
    /// Detach) and gives us a single auditable verb. Pass
    /// teamId=null to detach; owner-only.
    /// </summary>
    [HttpPut("{id:guid}/team")]
    public async Task<ActionResult<DocumentDto>> SetDocumentTeam(Guid id, [FromBody] SetDocumentTeamDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var document = await _documentService.SetDocumentTeamAsync(id, userId, dto.TeamId);
        if (document == null) return NotFound();
        await _auditService.LogAsync(
            dto.TeamId.HasValue ? "document.team.attach" : "document.team.detach",
            "Document",
            id.ToString());
        return Ok(document);
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
        var result = await _documentService.ShareDocumentAsync(id, userId, dto);
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

    /// <summary>
    /// Get paginated list of trashed documents
    /// </summary>
    [HttpGet("trash")]
    public async Task<ActionResult<PaginatedResult<TrashDocumentDto>>> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _documentService.GetTrashDocumentsPaginatedAsync(userId, page, pageSize);

        _logger.LogInformation(
            "[Documents] GET trash: count={DocumentCount}/{TotalCount}, page={Page}/{TotalPages}",
            result.Items.Count, result.TotalCount, result.Page, result.TotalPages);

        return Ok(result);
    }

    /// <summary>
    /// Restore a document from trash
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult> RestoreDocument(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _documentService.RestoreDocumentAsync(id, userId);
        if (!result) return NotFound();

        await _auditService.LogAsync("document.restore", "Document", id.ToString());
        return NoContent();
    }

    /// <summary>
    /// Permanently delete a document from trash
    /// </summary>
    [HttpDelete("{id:guid}/permanent")]
    public async Task<ActionResult> PermanentDeleteDocument(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _documentService.PermanentDeleteDocumentAsync(id, userId);
        if (!result) return NotFound();

        await _auditService.LogAsync("document.permanent_delete", "Document", id.ToString());
        return NoContent();
    }
}
