using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/[controller]")]
[Authorize]
public class BibliographyController : ControllerBase
{
    private readonly IBibliographyService _bibliographyService;
    private readonly IDocumentService _documentService;

    public BibliographyController(IBibliographyService bibliographyService, IDocumentService documentService)
    {
        _bibliographyService = bibliographyService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<BibliographyEntryDto>>> GetEntries(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var entries = await _bibliographyService.GetEntriesAsync(docId);
        return Ok(entries);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BibliographyEntryDto>> GetEntry(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var entry = await _bibliographyService.GetEntryAsync(docId, id);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [HttpPost]
    public async Task<ActionResult<BibliographyEntryDto>> CreateEntry(Guid docId, [FromBody] CreateBibliographyEntryDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var entry = await _bibliographyService.CreateEntryAsync(docId, dto);
        return CreatedAtAction(nameof(GetEntry), new { docId, id = entry.Id }, entry);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BibliographyEntryDto>> UpdateEntry(Guid docId, Guid id, [FromBody] UpdateBibliographyEntryDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var entry = await _bibliographyService.UpdateEntryAsync(docId, id, dto);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteEntry(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _bibliographyService.DeleteEntryAsync(docId, id);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<ActionResult<List<BibliographyEntryDto>>> ImportBibTex(Guid docId, [FromBody] ImportBibTexDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var entries = await _bibliographyService.ImportBibTexAsync(docId, dto.BibTexContent);
        return Ok(entries);
    }

    [HttpGet("export")]
    public async Task<ActionResult<string>> ExportBibTex(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var bibTex = await _bibliographyService.ExportBibTexAsync(docId);
        return Content(bibTex, "text/plain");
    }

    [HttpPost("doi")]
    public async Task<ActionResult<DoiLookupResultDto>> LookupDoi([FromBody] DoiLookupDto dto)
    {
        var result = await _bibliographyService.LookupDoiAsync(dto.Doi);
        if (result == null) return NotFound("DOI not found");
        return Ok(result);
    }
}
