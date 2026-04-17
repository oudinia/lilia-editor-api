using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/export")]
[Authorize]
public class ExportController : ControllerBase
{
    private readonly ILaTeXExportService _latexExportService;
    private readonly IDocumentExportService _documentExportService;
    private readonly IDocumentService _documentService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        ILaTeXExportService latexExportService,
        IDocumentExportService documentExportService,
        IDocumentService documentService,
        ILogger<ExportController> logger)
    {
        _latexExportService = latexExportService;
        _documentExportService = documentExportService;
        _documentService = documentService;
        _logger = logger;
    }

    /// <summary>
    /// Export document as a LaTeX project ZIP file
    /// </summary>
    [HttpGet("latex")]
    public async Task<IActionResult> ExportLatex(Guid docId, [FromQuery] LaTeXExportOptions options)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null)
            return NotFound();

        _logger.LogInformation("[Export] LaTeX export for document {DocId} by user {UserId}", docId, userId);

        var zipStream = await _latexExportService.ExportToZipAsync(docId, options);

        var sanitizedTitle = SanitizeFilename(document.Title);
        return File(zipStream, "application/zip", $"{sanitizedTitle}.zip");
    }

    /// <summary>
    /// Export document as a DOCX file
    /// </summary>
    [HttpGet("docx")]
    public async Task<IActionResult> ExportDocx(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null)
            return NotFound();

        _logger.LogInformation("[Export] DOCX export for document {DocId} by user {UserId}", docId, userId);

        var docxBytes = await _documentExportService.ExportToDocxAsync(docId);

        var sanitizedTitle = SanitizeFilename(document.Title);
        return File(docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{sanitizedTitle}.docx");
    }

    /// <summary>
    /// Export document as a PDF file
    /// </summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> ExportPdf(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null)
            return NotFound();

        _logger.LogInformation("[Export] PDF export for document {DocId} by user {UserId}", docId, userId);

        try
        {
            var pdfBytes = await _documentExportService.ExportToPdfAsync(docId);
            var sanitizedTitle = SanitizeFilename(document.Title);
            return File(pdfBytes, "application/pdf", $"{sanitizedTitle}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Export] PDF compilation failed for document {DocId}", docId);
            // Return a JSON body so the frontend can display the error message
            // (PdfPreview.tsx reads response.json().message)
            return StatusCode(500, new { message = ex.Message, error = "PDF compilation failed" });
        }
    }

    private string? GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }

    private static string SanitizeFilename(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }
}
