using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/preview")]
[Authorize]
public class PreviewController : ControllerBase
{
    private readonly IRenderService _renderService;
    private readonly IPreviewCacheService _cacheService;
    private readonly IDocumentService _documentService;

    public PreviewController(
        IRenderService renderService,
        IPreviewCacheService cacheService,
        IDocumentService documentService)
    {
        _renderService = renderService;
        _cacheService = cacheService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Get the total page count for the document preview
    /// </summary>
    [HttpGet("page-count")]
    public async Task<ActionResult<PageCountResponse>> GetPageCount(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Verify user has access to document
        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        var count = await _renderService.GetPageCountAsync(docId);
        return Ok(new PageCountResponse(count));
    }

    /// <summary>
    /// Get the sections (headings) for navigation
    /// </summary>
    [HttpGet("sections")]
    public async Task<ActionResult<SectionsResponse>> GetSections(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        var sections = await _renderService.GetSectionsAsync(docId);
        return Ok(new SectionsResponse(sections));
    }

    /// <summary>
    /// Get rendered HTML for a specific page
    /// </summary>
    [HttpGet("html")]
    public async Task<ActionResult<PreviewResponse>> GetHtmlPreview(
        Guid docId,
        [FromQuery] int page = 1)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        // Check cache first
        var cached = await _cacheService.GetCachedPreviewAsync(docId, "html", page);
        var cacheKey = $"preview:{docId}:html:page{page}";

        if (cached != null)
        {
            var totalPages = await _renderService.GetPageCountAsync(docId);
            return Ok(new PreviewResponse(
                cached,
                "html",
                page,
                totalPages,
                DateTime.UtcNow,
                cacheKey
            ));
        }

        // Render the page
        var content = await _renderService.RenderPageAsync(docId, page);
        var pageCount = await _renderService.GetPageCountAsync(docId);

        // Cache the result
        await _cacheService.SetCachedPreviewAsync(docId, "html", content, page);

        return Ok(new PreviewResponse(
            content,
            "html",
            page,
            pageCount,
            DateTime.UtcNow,
            cacheKey
        ));
    }

    /// <summary>
    /// Get the full document rendered as LaTeX
    /// </summary>
    [HttpGet("latex")]
    public async Task<ActionResult<PreviewResponse>> GetLatexPreview(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        // Check cache first
        var cached = await _cacheService.GetCachedPreviewAsync(docId, "latex");
        var cacheKey = $"preview:{docId}:latex";

        if (cached != null)
        {
            return Ok(new PreviewResponse(
                cached,
                "latex",
                null,
                null,
                DateTime.UtcNow,
                cacheKey
            ));
        }

        // Render the full document to LaTeX
        var content = await _renderService.RenderToLatexAsync(docId);

        // Cache the result
        await _cacheService.SetCachedPreviewAsync(docId, "latex", content);

        return Ok(new PreviewResponse(
            content,
            "latex",
            null,
            null,
            DateTime.UtcNow,
            cacheKey
        ));
    }

    /// <summary>
    /// Get the full document rendered as HTML (non-paginated)
    /// </summary>
    [HttpGet("html/full")]
    public async Task<ActionResult<PreviewResponse>> GetFullHtmlPreview(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        // Check cache first
        var cached = await _cacheService.GetCachedPreviewAsync(docId, "html-full");
        var cacheKey = $"preview:{docId}:html-full";

        if (cached != null)
        {
            return Ok(new PreviewResponse(
                cached,
                "html",
                null,
                null,
                DateTime.UtcNow,
                cacheKey
            ));
        }

        // Render the full document
        var content = await _renderService.RenderToHtmlAsync(docId);

        // Cache the result
        await _cacheService.SetCachedPreviewAsync(docId, "html-full", content);

        return Ok(new PreviewResponse(
            content,
            "html",
            null,
            null,
            DateTime.UtcNow,
            cacheKey
        ));
    }
}
