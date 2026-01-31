using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/[controller]")]
[Authorize]
public class VersionsController : ControllerBase
{
    private readonly IVersionService _versionService;
    private readonly IDocumentService _documentService;

    public VersionsController(IVersionService versionService, IDocumentService documentService)
    {
        _versionService = versionService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<VersionListDto>>> GetVersions(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var versions = await _versionService.GetVersionsAsync(docId);
        return Ok(versions);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VersionDto>> GetVersion(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var version = await _versionService.GetVersionAsync(docId, id);
        if (version == null) return NotFound();
        return Ok(version);
    }

    [HttpPost]
    public async Task<ActionResult<VersionDto>> CreateVersion(Guid docId, [FromBody] CreateVersionDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var version = await _versionService.CreateVersionAsync(docId, userId, dto);
        return CreatedAtAction(nameof(GetVersion), new { docId, id = version.Id }, version);
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<DocumentDto>> RestoreVersion(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var document = await _versionService.RestoreVersionAsync(docId, id, userId);
        if (document == null) return NotFound();
        return Ok(document);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteVersion(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _versionService.DeleteVersionAsync(docId, id, userId);
        if (!result) return NotFound();
        return NoContent();
    }
}
