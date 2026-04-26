using System.IO.Compression;
using System.Text;
using Lilia.Api.Services;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/export")]
[Authorize]
public class ExportController : ControllerBase
{
    private readonly ILaTeXExportService _latexExportService;
    private readonly IDocumentExportService _documentExportService;
    private readonly IDocumentService _documentService;
    private readonly IRenderService _renderService;
    private readonly LiliaDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        ILaTeXExportService latexExportService,
        IDocumentExportService documentExportService,
        IDocumentService documentService,
        IRenderService renderService,
        LiliaDbContext dbContext,
        IStorageService storageService,
        ILogger<ExportController> logger)
    {
        _latexExportService = latexExportService;
        _documentExportService = documentExportService;
        _documentService = documentService;
        _renderService = renderService;
        _dbContext = dbContext;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Export document as LaTeX. Three response modes via ?mode= query:
    ///   - <c>zip</c> (default): full project zip with main.tex, references.bib (if any), README.txt.
    ///                           Suits multi-file Overleaf workflows.
    ///   - <c>tex</c>: just main.tex as a downloadable single file. Suits single-file power users
    ///                 who don't want the zip wrapper for a doc with no extra assets.
    ///   - <c>preview</c>: just main.tex inline (text/plain), no Content-Disposition.
    ///                     Browser shows it as text. Suits in-app preview / "view source" UIs.
    ///
    /// All other LaTeXExportOptions still apply (structure, bibliographyStyle, etc.) for the
    /// zip mode; tex/preview always emit a single-file rendering regardless of the structure
    /// option.
    /// </summary>
    [HttpGet("latex")]
    public async Task<IActionResult> ExportLatex(
        Guid docId,
        [FromQuery] LaTeXExportOptions options,
        [FromQuery] string? mode = null)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null)
            return NotFound();

        var sanitizedTitle = SanitizeFilename(document.Title);
        var modeNorm = (mode ?? "zip").Trim().ToLowerInvariant();

        if (modeNorm == "tex" || modeNorm == "preview")
        {
            _logger.LogInformation("[Export] LaTeX {Mode} export for document {DocId} by user {UserId}", modeNorm, docId, userId);

            // Load blocks + bibliography to call the in-memory helper (no zip step).
            var docWithBlocks = await _dbContext.Documents
                .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
                .Include(d => d.BibliographyEntries)
                .FirstOrDefaultAsync(d => d.Id == docId);
            if (docWithBlocks == null) return NotFound();

            var texContent = _latexExportService.BuildSingleFileLatex(
                docWithBlocks,
                docWithBlocks.Blocks.OrderBy(b => b.SortOrder).ToList(),
                docWithBlocks.BibliographyEntries?.ToList() ?? new(),
                options);

            var bytes = System.Text.Encoding.UTF8.GetBytes(texContent);

            if (modeNorm == "preview")
            {
                // Inline render in the browser. No Content-Disposition so
                // the response is shown as text rather than downloaded.
                return File(bytes, "text/plain; charset=utf-8");
            }

            // Direct .tex download.
            return File(bytes, "application/x-tex", $"{sanitizedTitle}.tex");
        }

        if (modeNorm != "zip")
        {
            return BadRequest(new { error = $"Unknown mode '{mode}'. Allowed: zip (default), tex, preview." });
        }

        _logger.LogInformation("[Export] LaTeX zip export for document {DocId} by user {UserId}", docId, userId);

        var zipStream = await _latexExportService.ExportToZipAsync(docId, options);
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
            if (pdfBytes == null || pdfBytes.Length < 100 || !pdfBytes.AsSpan(0, 4).SequenceEqual("%PDF"u8))
            {
                _logger.LogError("[Export] PDF for document {DocId} is empty or invalid ({Length} bytes) — tolerant compile produced no output", docId, pdfBytes?.Length ?? 0);
                return StatusCode(500, new { message = "LaTeX produced an empty PDF. The document body is likely unsupported (e.g. pure TikZ with vendor-custom .sty).", error = "PDF compilation produced empty output" });
            }
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

    /// <summary>
    /// Export document as Markdown.
    /// </summary>
    [HttpGet("markdown")]
    public async Task<IActionResult> ExportMarkdown(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        var markdown = await _renderService.RenderToMarkdownAsync(docId);
        var bytes = Encoding.UTF8.GetBytes(markdown);
        return File(bytes, "text/markdown", $"{SanitizeFilename(document.Title)}.md");
    }

    /// <summary>
    /// Export document as LML (Lilia Markup Language) source.
    /// </summary>
    [HttpGet("lml")]
    public async Task<IActionResult> ExportLml(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        var lml = await _renderService.RenderToLmlAsync(docId);
        var bytes = Encoding.UTF8.GetBytes(lml);
        return File(bytes, "text/plain", $"{SanitizeFilename(document.Title)}.lml");
    }

    /// <summary>
    /// Export document as HTML — either a single .html file or a .zip bundle with images.
    /// </summary>
    [HttpGet("html")]
    public async Task<IActionResult> ExportHtml(Guid docId, [FromQuery] bool bundleImages = false)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        var html = await _renderService.RenderToHtmlAsync(docId);
        var title = SanitizeFilename(document.Title);
        var htmlDocument = $"<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>{System.Net.WebUtility.HtmlEncode(document.Title)}</title></head><body>\n{html}\n</body></html>\n";

        if (!bundleImages)
        {
            var bytes = Encoding.UTF8.GetBytes(htmlDocument);
            return File(bytes, "text/html", $"{title}.html");
        }

        // Build a ZIP: document.html + images/*
        var assets = await _dbContext.Assets
            .Where(a => a.DocumentId == docId)
            .ToListAsync();

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var htmlWithLocalImages = htmlDocument;
            foreach (var asset in assets)
            {
                var localPath = $"images/{asset.Id}{System.IO.Path.GetExtension(asset.FileName)}";
                // Rewrite references in the HTML to point to the bundled file
                if (!string.IsNullOrEmpty(asset.Url))
                {
                    htmlWithLocalImages = htmlWithLocalImages.Replace(asset.Url, localPath);
                }
                htmlWithLocalImages = htmlWithLocalImages.Replace($"/api/documents/{docId}/assets/{asset.Id}", localPath);

                // Add asset bytes to ZIP
                try
                {
                    await using var assetStream = await _storageService.DownloadAsync(asset.StorageKey);
                    var entry = archive.CreateEntry(localPath, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await assetStream.CopyToAsync(entryStream);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Export] Failed to bundle asset {AssetId} for document {DocId}", asset.Id, docId);
                }
            }

            var htmlEntry = archive.CreateEntry("document.html", CompressionLevel.Optimal);
            await using var htmlStream = htmlEntry.Open();
            await htmlStream.WriteAsync(Encoding.UTF8.GetBytes(htmlWithLocalImages));
        }
        ms.Position = 0;
        return File(ms, "application/zip", $"{title}.zip");
    }

    private string? GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }

    // Strip OS-unsafe filename chars regardless of host platform.
    // Path.GetInvalidFileNameChars() returns only `/` and `\0` on Linux,
    // which is too lenient for filenames we send back to the browser
    // (the user may be on Windows / Android / iOS). Replace anything
    // in the cross-platform unsafe set + control chars + collapse
    // whitespace.
    private static readonly char[] InvalidFilenameChars =
        new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0' }
        .Concat(Enumerable.Range(1, 31).Select(c => (char)c))
        .ToArray();

    private static string SanitizeFilename(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "document";
        var sanitized = new string(title
            .Select(c => InvalidFilenameChars.Contains(c) ? '_' : c)
            .ToArray());
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s+", "_");
        sanitized = sanitized.Trim('_', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }
}
