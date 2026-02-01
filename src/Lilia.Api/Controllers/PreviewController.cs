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
    private readonly ILogger<PreviewController> _logger;

    public PreviewController(
        IRenderService renderService,
        IPreviewCacheService cacheService,
        IDocumentService documentService,
        ILogger<PreviewController> logger)
    {
        _renderService = renderService;
        _cacheService = cacheService;
        _documentService = documentService;
        _logger = logger;
    }

    private string? GetUserId()
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        _logger.LogDebug(
            "[Preview] Auth check - IsAuthenticated: {IsAuth}, UserId: {UserId}, Claims: [{Claims}]",
            User.Identity?.IsAuthenticated ?? false,
            userId ?? "NULL",
            string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}"))
        );

        return userId;
    }

    /// <summary>
    /// Get the total page count for the document preview
    /// </summary>
    [HttpGet("page-count")]
    public async Task<ActionResult<PageCountResponse>> GetPageCount(Guid docId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogDebug("[Preview] GET page-count for document {DocId}", docId);

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[Preview] Unauthorized: No user ID found in claims for document {DocId}", docId);
            return Unauthorized(new { error = "No valid authentication token provided" });
        }

        // Verify user has access to document
        var authTime = sw.ElapsedMilliseconds;
        var document = await _documentService.GetDocumentAsync(docId, userId);
        var docTime = sw.ElapsedMilliseconds;
        if (document == null)
        {
            _logger.LogWarning("[Preview] Document {DocId} not found or user {UserId} has no access", docId, userId);
            return NotFound();
        }

        var count = await _renderService.GetPageCountAsync(docId);
        var renderTime = sw.ElapsedMilliseconds;

        _logger.LogInformation("[Preview] page-count for {DocId}: auth={AuthMs}ms, doc={DocMs}ms, render={RenderMs}ms, total={TotalMs}ms",
            docId, authTime, docTime - authTime, renderTime - docTime, renderTime);
        return Ok(new PageCountResponse(count));
    }

    private void LogAuthHeader()
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader))
        {
            _logger.LogWarning("[Preview] No Authorization header present in request");
        }
        else
        {
            var tokenPreview = authHeader.Length > 50
                ? authHeader.Substring(0, 50) + "..."
                : authHeader;
            _logger.LogDebug("[Preview] Authorization header: {TokenPreview}", tokenPreview);
        }
    }

    /// <summary>
    /// Get the sections (headings) for navigation
    /// </summary>
    [HttpGet("sections")]
    public async Task<ActionResult<SectionsResponse>> GetSections(Guid docId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogDebug("[Preview] GET sections for document {DocId}", docId);

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[Preview] Unauthorized: No user ID found for sections request on {DocId}", docId);
            return Unauthorized(new { error = "No valid authentication token provided" });
        }

        var authTime = sw.ElapsedMilliseconds;
        var document = await _documentService.GetDocumentAsync(docId, userId);
        var docTime = sw.ElapsedMilliseconds;
        if (document == null)
        {
            _logger.LogWarning("[Preview] Document {DocId} not found for sections request", docId);
            return NotFound();
        }

        var sections = await _renderService.GetSectionsAsync(docId);
        var renderTime = sw.ElapsedMilliseconds;

        _logger.LogInformation("[Preview] sections for {DocId}: auth={AuthMs}ms, doc={DocMs}ms, render={RenderMs}ms, total={TotalMs}ms, count={Count}",
            docId, authTime, docTime - authTime, renderTime - docTime, renderTime, sections.Count);
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogDebug("[Preview] GET html page {Page} for document {DocId}", page, docId);

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[Preview] Unauthorized: No user ID found for HTML preview on {DocId}", docId);
            return Unauthorized(new { error = "No valid authentication token provided" });
        }

        var authTime = sw.ElapsedMilliseconds;
        var document = await _documentService.GetDocumentAsync(docId, userId);
        var docTime = sw.ElapsedMilliseconds;
        if (document == null)
        {
            _logger.LogWarning("[Preview] Document {DocId} not found for HTML preview", docId);
            return NotFound();
        }

        // Check cache first
        var cached = await _cacheService.GetCachedPreviewAsync(docId, "html", page);
        var cacheTime = sw.ElapsedMilliseconds;
        var cacheKey = $"preview:{docId}:html:page{page}";

        if (cached != null)
        {
            var totalPages = await _renderService.GetPageCountAsync(docId);
            _logger.LogInformation("[Preview] html (CACHED) for {DocId} page {Page}: auth={AuthMs}ms, doc={DocMs}ms, cache={CacheMs}ms, total={TotalMs}ms",
                docId, page, authTime, docTime - authTime, cacheTime - docTime, sw.ElapsedMilliseconds);
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
        var renderTime = sw.ElapsedMilliseconds;
        var pageCount = await _renderService.GetPageCountAsync(docId);
        var countTime = sw.ElapsedMilliseconds;

        // Cache the result
        await _cacheService.SetCachedPreviewAsync(docId, "html", content, page);
        var finalTime = sw.ElapsedMilliseconds;

        _logger.LogInformation("[Preview] html for {DocId} page {Page}: auth={AuthMs}ms, doc={DocMs}ms, cache-check={CacheMs}ms, render={RenderMs}ms, count={CountMs}ms, cache-set={CacheSetMs}ms, total={TotalMs}ms",
            docId, page, authTime, docTime - authTime, cacheTime - docTime, renderTime - cacheTime, countTime - renderTime, finalTime - countTime, finalTime);

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
        _logger.LogDebug("[Preview] GET latex for document {DocId}", docId);
        LogAuthHeader();

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[Preview] Unauthorized: No user ID found for LaTeX preview on {DocId}", docId);
            return Unauthorized(new { error = "No valid authentication token provided" });
        }

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null)
        {
            _logger.LogWarning("[Preview] Document {DocId} not found for LaTeX preview", docId);
            return NotFound();
        }

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
        _logger.LogDebug("[Preview] GET full html for document {DocId}", docId);
        LogAuthHeader();

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[Preview] Unauthorized: No user ID found for full HTML preview on {DocId}", docId);
            return Unauthorized(new { error = "No valid authentication token provided" });
        }

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null)
        {
            _logger.LogWarning("[Preview] Document {DocId} not found for full HTML preview", docId);
            return NotFound();
        }

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
