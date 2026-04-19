using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Structural-findings endpoints for already-imported documents. Mirrors the
/// session-side endpoints on ImportReviewController — any finalised import
/// can still be scanned for hints (useful for users whose CV landed badly on
/// the legacy import path and wants to fix it without re-uploading).
/// </summary>
[ApiController]
[Route("api/documents/{documentId:guid}/hints")]
[Authorize]
public class DocumentHintsController : ControllerBase
{
    private readonly IImportHintService _hintService;
    private readonly LiliaDbContext _context;

    public DocumentHintsController(IImportHintService hintService, LiliaDbContext context)
    {
        _hintService = hintService;
        _context = context;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>List structural findings for this document (pure SELECT).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ImportStructuralFindingDto>>> List(Guid documentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _hintService.ListForDocumentAsync(documentId, userId));
    }

    /// <summary>(Re)compute findings. Clears pending rows, reruns rules, bulk-inserts.</summary>
    [HttpPost("compute")]
    public async Task<ActionResult<ComputeHintsResponseDto>> Compute(Guid documentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var count = await _hintService.ComputeForDocumentAsync(documentId, userId);
        return Ok(new ComputeHintsResponseDto(count));
    }

    [HttpPost("{findingId:guid}/apply")]
    public async Task<IActionResult> Apply(Guid documentId, Guid findingId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return await _hintService.ApplyAsync(findingId, userId) ? Ok() : NotFound();
    }

    [HttpPost("{findingId:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid documentId, Guid findingId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return await _hintService.DismissAsync(findingId, userId) ? Ok() : NotFound();
    }

    /// <summary>Set the document category. Owner-only.</summary>
    [HttpPatch("/api/documents/{documentId:guid}/category")]
    public async Task<IActionResult> SetCategory(Guid documentId, [FromBody] SetDocumentCategoryDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var affected = await _context.Documents
            .IgnoreQueryFilters()
            .Where(d => d.Id == documentId && d.OwnerId == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.DocumentCategory, dto.Category)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        return affected > 0 ? Ok() : NotFound();
    }
}
