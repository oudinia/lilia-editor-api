using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/typst")]
[Authorize]
public class TypstController : ControllerBase
{
    private readonly ITypstRenderService _typstService;
    private readonly ILogger<TypstController> _logger;

    public TypstController(
        ITypstRenderService typstService,
        ILogger<TypstController> logger)
    {
        _typstService = typstService;
        _logger = logger;
    }

    /// <summary>
    /// Compile Typst source to PDF.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> CompileTypstPreview([FromBody] CompileTypstRequest request)
    {
        if (!_typstService.IsAvailable)
            return StatusCode(503, new { error = "Typst binary is not available on this server" });

        try
        {
            var pdf = await _typstService.CompileTypstToPdfAsync(request.Source, request.Timeout);
            return File(pdf, "application/pdf", "preview.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile Typst preview");
            return BadRequest(new { error = ex.Message });
        }
    }

    // NOTE: a `GET /api/documents/{documentId:guid}/preview/typst` route used
    // to live here. It collided with PreviewController.GetTypstPreview, which
    // is the canonical preview endpoint — it goes through TypstExporter
    // (documentclass-aware), supports ?format=svg|pdf|png for inline preview
    // vs download, and returns 503 with structured fallback hints. Removing
    // the duplicate here resolves the AmbiguousMatchException (LILIA-135).
    // Continue using PreviewController for the preview path.

    /// <summary>
    /// Export a document as Typst source (.typ file).
    /// </summary>
    [HttpGet("/api/documents/{documentId:guid}/export/typst")]
    public async Task<IActionResult> ExportDocumentTypst(Guid documentId)
    {
        try
        {
            var typstSource = await _typstService.RenderToTypstAsync(documentId);
            return Content(typstSource, "text/plain", System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export document {DocumentId} as Typst", documentId);
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record CompileTypstRequest(string Source, int Timeout = 10);
