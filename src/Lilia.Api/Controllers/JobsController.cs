using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lilia.Api.Services;
using Lilia.Core.Interfaces;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/jobs")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IRenderService _renderService;
    private readonly IDocumentService _documentService;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IRenderService renderService,
        IDocumentService documentService,
        ILogger<JobsController> logger)
    {
        _renderService = renderService;
        _documentService = documentService;
        _logger = logger;
    }

    public record ExportRequest(
        Guid DocumentId,
        string Format,
        Dictionary<string, object>? Options = null
    );

    public record ExportResponse(
        JobInfo Job,
        string Content,
        string Filename
    );

    public record JobInfo(
        string Id,
        string Status,
        int Progress
    );

    /// <summary>
    /// Export a document to the specified format
    /// </summary>
    [HttpPost("export")]
    public async Task<ActionResult<ExportResponse>> Export([FromBody] ExportRequest request)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        _logger.LogInformation(
            "[Export] User {UserId} exporting document {DocumentId} to {Format}",
            userId, request.DocumentId, request.Format);

        try
        {
            // Verify document access
            var document = await _documentService.GetDocumentAsync(request.DocumentId, userId);
            if (document == null)
            {
                return NotFound(new { message = "Document not found" });
            }

            var jobId = Guid.NewGuid().ToString();
            string content;
            string filename;
            string extension;

            switch (request.Format.ToUpperInvariant())
            {
                case "LATEX":
                    content = await _renderService.RenderToLatexAsync(request.DocumentId);
                    extension = "tex";
                    break;

                case "HTML":
                    content = await _renderService.RenderToHtmlAsync(request.DocumentId);
                    extension = "html";
                    break;

                case "MARKDOWN":
                    content = await RenderToMarkdownAsync(request.DocumentId);
                    extension = "md";
                    break;

                case "LML":
                    content = await RenderToLmlAsync(request.DocumentId);
                    extension = "lilia";
                    break;

                case "PDF":
                    // PDF is handled client-side via print preview
                    return BadRequest(new { message = "PDF export should use the print preview at /document/{id}/print" });

                default:
                    return BadRequest(new { message = $"Unsupported format: {request.Format}" });
            }

            // Sanitize filename
            var safeTitle = string.IsNullOrWhiteSpace(document.Title)
                ? "document"
                : SanitizeFilename(document.Title);
            filename = $"{safeTitle}.{extension}";

            _logger.LogInformation(
                "[Export] Successfully exported document {DocumentId} to {Format}, size={Size} bytes",
                request.DocumentId, request.Format, content.Length);

            return Ok(new ExportResponse(
                new JobInfo(jobId, "completed", 100),
                content,
                filename
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Export] Failed to export document {DocumentId}", request.DocumentId);
            return StatusCode(500, new { message = "Export failed: " + ex.Message });
        }
    }

    private async Task<string> RenderToMarkdownAsync(Guid documentId)
    {
        // Simple conversion: get HTML and convert to basic markdown
        // For now, return a placeholder - full implementation would need a proper converter
        var html = await _renderService.RenderToHtmlAsync(documentId);

        // Basic HTML to Markdown conversion
        var markdown = html
            .Replace("<h1>", "# ").Replace("</h1>", "\n\n")
            .Replace("<h2>", "## ").Replace("</h2>", "\n\n")
            .Replace("<h3>", "### ").Replace("</h3>", "\n\n")
            .Replace("<h4>", "#### ").Replace("</h4>", "\n\n")
            .Replace("<p>", "").Replace("</p>", "\n\n")
            .Replace("<strong>", "**").Replace("</strong>", "**")
            .Replace("<em>", "_").Replace("</em>", "_")
            .Replace("<code>", "`").Replace("</code>", "`")
            .Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n")
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&amp;", "&");

        // Remove remaining HTML tags
        markdown = System.Text.RegularExpressions.Regex.Replace(markdown, "<[^>]+>", "");

        return markdown.Trim();
    }

    private async Task<string> RenderToLmlAsync(Guid documentId)
    {
        // LML is Lilia's native JSON format
        var document = await _documentService.GetDocumentAsync(documentId,
            User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value ?? "");

        if (document == null)
        {
            return "{}";
        }

        var lml = new
        {
            version = "1.0",
            document = new
            {
                id = document.Id,
                title = document.Title,
                createdAt = document.CreatedAt,
                updatedAt = document.UpdatedAt,
                blocks = document.Blocks?.Select(b => new
                {
                    id = b.Id,
                    type = b.Type,
                    content = b.Content,
                    sortOrder = b.SortOrder,
                    depth = b.Depth
                })
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(lml, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", filename.Where(c => !invalid.Contains(c)));

        // Limit length and trim whitespace
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized.Trim();
    }
}
