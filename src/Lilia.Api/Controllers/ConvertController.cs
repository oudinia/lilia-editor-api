using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace Lilia.Api.Controllers;

/// <summary>
/// Public document conversion API - no authentication required.
/// Rate limited by IP address for anonymous users, by user ID for authenticated users.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class ConvertController : ControllerBase
{
    private readonly IDocxImportService _docxImportService;
    private readonly IDocxExportService _docxExportService;
    private readonly ILatexParser _latexParser;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ConvertController> _logger;

    private const int AnonymousLimitPerDay = 3;
    private const int AuthenticatedLimitPerDay = 10;
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20MB
    private const int MaxLatexTextLength = 100_000;

    public ConvertController(
        IDocxImportService docxImportService,
        IDocxExportService docxExportService,
        ILatexParser latexParser,
        IDistributedCache cache,
        ILogger<ConvertController> logger)
    {
        _docxImportService = docxImportService;
        _docxExportService = docxExportService;
        _latexParser = latexParser;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Convert DOCX to LaTeX format.
    /// </summary>
    [HttpPost("docx-to-latex")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertDocxToLatex(
        IFormFile file,
        [FromForm] string? options = null)
    {
        return await HandleDocxConversion(file, "latex", options);
    }

    /// <summary>
    /// Convert DOCX to HTML format.
    /// </summary>
    [HttpPost("docx-to-html")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertDocxToHtml(IFormFile file)
    {
        return await HandleDocxConversion(file, "html", null);
    }

    /// <summary>
    /// Convert DOCX to Markdown format.
    /// </summary>
    [HttpPost("docx-to-markdown")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertDocxToMarkdown(IFormFile file)
    {
        return await HandleDocxConversion(file, "markdown", null);
    }

    /// <summary>
    /// Convert DOCX to PDF format (returns HTML for browser printing).
    /// </summary>
    [HttpPost("docx-to-pdf")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertDocxToPdf(IFormFile file)
    {
        return await HandleDocxConversion(file, "html", null);
    }

    /// <summary>
    /// Convert LaTeX to DOCX format.
    /// </summary>
    [HttpPost("latex-to-docx")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertLatexToDocx(IFormFile file)
    {
        return await HandleLatexConversion(file, "docx");
    }

    /// <summary>
    /// Convert Markdown to LaTeX format.
    /// </summary>
    [HttpPost("markdown-to-latex")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertMarkdownToLatex(
        IFormFile file,
        [FromForm] string? options = null)
    {
        return await HandleMarkdownConversion(file, "latex", options);
    }

    /// <summary>
    /// Get conversion job status.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(JobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatus(Guid jobId)
    {
        var cached = await _cache.GetAsync($"conversion:{jobId}");
        if (cached != null)
        {
            return Ok(new JobStatusResponse
            {
                JobId = jobId,
                Status = "COMPLETED",
                Progress = 100,
                DownloadUrl = $"/api/convert/jobs/{jobId}/download"
            });
        }

        return NotFound(new ErrorResponse { Message = "Job not found", Code = "JOB_NOT_FOUND" });
    }

    /// <summary>
    /// Download conversion result.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadResult(Guid jobId)
    {
        var key = $"conversion:{jobId}";
        var cached = await _cache.GetAsync(key);
        if (cached != null)
        {
            var result = JsonSerializer.Deserialize<CachedConversionResult>(cached)!;

            // Remove from cache after download
            await _cache.RemoveAsync(key);

            return File(result.Content, result.ContentType, result.Filename);
        }

        return NotFound(new ErrorResponse { Message = "Download not available", Code = "NOT_FOUND" });
    }

    /// <summary>
    /// Get remaining conversion quota for the current user/IP.
    /// </summary>
    [HttpGet("quota")]
    [ProducesResponseType(typeof(QuotaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuota()
    {
        var identifier = GetRateLimitIdentifier();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var limit = isAuthenticated ? AuthenticatedLimitPerDay : AnonymousLimitPerDay;

        var entry = await GetOrCreateRateLimitEntryAsync(identifier);
        var remaining = Math.Max(0, limit - entry.Count);

        return Ok(new QuotaResponse
        {
            Limit = limit,
            Remaining = remaining,
            ResetsAt = entry.ResetTime,
            IsAuthenticated = isAuthenticated
        });
    }

    private async Task<IActionResult> HandleDocxConversion(IFormFile? file, string targetFormat, string? options)
    {
        var sw = Stopwatch.StartNew();
        var conversionType = $"docx-to-{targetFormat}";

        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Message = "No file provided", Code = "NO_FILE" });
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (extension != ".docx")
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid file type. Expected: .docx",
                Code = "INVALID_FORMAT"
            });
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!string.IsNullOrEmpty(contentType)
            && contentType != "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            && contentType != "application/octet-stream")
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid MIME type for DOCX file",
                Code = "INVALID_MIME_TYPE"
            });
        }

        var rateLimitResult = await CheckRateLimitAsync();
        if (rateLimitResult != null)
        {
            return rateLimitResult;
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var importResult = await _docxImportService.ImportAsync(tempPath);

                if (!importResult.Success)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = importResult.ErrorMessage ?? "Failed to parse DOCX",
                        Code = "PARSE_ERROR"
                    });
                }

                string content;
                string outputContentType;
                string fileExtension;

                switch (targetFormat)
                {
                    case "latex":
                        content = RenderImportDocumentToLatex(importResult);
                        outputContentType = "application/x-tex";
                        fileExtension = ".tex";
                        break;
                    case "html":
                        content = RenderImportDocumentToHtml(importResult);
                        outputContentType = "text/html";
                        fileExtension = ".html";
                        break;
                    case "markdown":
                        content = RenderImportDocumentToMarkdown(importResult);
                        outputContentType = "text/markdown";
                        fileExtension = ".md";
                        break;
                    default:
                        return BadRequest(new ErrorResponse { Message = $"Unsupported target format: {targetFormat}", Code = "INVALID_FORMAT" });
                }

                await IncrementRateLimitAsync();

                var jobId = Guid.NewGuid();
                var filename = Path.GetFileNameWithoutExtension(file.FileName) + fileExtension;
                await StoreConversionResultAsync(jobId, Encoding.UTF8.GetBytes(content), outputContentType, filename);

                _logger.LogInformation(
                    "[Convert] {ConversionType}: file={FileName}, size={SizeKB}KB, elapsed={ElapsedMs}ms",
                    conversionType, file.FileName, file.Length / 1024, sw.ElapsedMilliseconds);

                return Ok(new ConversionResponse
                {
                    JobId = jobId,
                    Status = "completed",
                    DownloadUrl = $"/api/convert/jobs/{jobId}/download",
                    PollUrl = $"/api/convert/jobs/{jobId}"
                });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Convert] {ConversionType} failed for {FileName}: {Message}",
                conversionType, file.FileName, ex.Message);
            return StatusCode(500, new ErrorResponse
            {
                Message = $"Conversion failed: {ex.Message}",
                Code = "INTERNAL_ERROR"
            });
        }
    }

    private async Task<IActionResult> HandleLatexConversion(IFormFile? file, string targetFormat)
    {
        var sw = Stopwatch.StartNew();
        var conversionType = $"latex-to-{targetFormat}";

        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Message = "No file provided", Code = "NO_FILE" });
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (extension != ".tex" && extension != ".latex")
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid file type. Expected: .tex or .latex",
                Code = "INVALID_FORMAT"
            });
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!string.IsNullOrEmpty(contentType)
            && contentType != "application/x-tex"
            && contentType != "text/x-tex"
            && contentType != "text/plain"
            && contentType != "application/octet-stream")
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid MIME type for LaTeX file",
                Code = "INVALID_MIME_TYPE"
            });
        }

        var rateLimitResult = await CheckRateLimitAsync();
        if (rateLimitResult != null)
        {
            return rateLimitResult;
        }

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var latexContent = await reader.ReadToEndAsync();

            var exportDoc = ParseLatexToExportDocument(latexContent);
            var docxBytes = await _docxExportService.ExportAsync(exportDoc);

            await IncrementRateLimitAsync();

            var jobId = Guid.NewGuid();
            var filename = Path.GetFileNameWithoutExtension(file.FileName) + ".docx";
            await StoreConversionResultAsync(jobId, docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", filename);

            _logger.LogInformation(
                "[Convert] {ConversionType}: file={FileName}, size={SizeKB}KB, elapsed={ElapsedMs}ms",
                conversionType, file.FileName, file.Length / 1024, sw.ElapsedMilliseconds);

            return Ok(new ConversionResponse
            {
                JobId = jobId,
                Status = "completed",
                DownloadUrl = $"/api/convert/jobs/{jobId}/download",
                PollUrl = $"/api/convert/jobs/{jobId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Convert] {ConversionType} failed for {FileName}: {Message}",
                conversionType, file.FileName, ex.Message);
            return StatusCode(500, new ErrorResponse
            {
                Message = $"Conversion failed: {ex.Message}",
                Code = "INTERNAL_ERROR"
            });
        }
    }

    private async Task<IActionResult> HandleMarkdownConversion(IFormFile? file, string targetFormat, string? options)
    {
        var sw = Stopwatch.StartNew();
        var conversionType = $"markdown-to-{targetFormat}";

        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Message = "No file provided", Code = "NO_FILE" });
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (extension != ".md" && extension != ".markdown")
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid file type. Expected: .md or .markdown",
                Code = "INVALID_FORMAT"
            });
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!string.IsNullOrEmpty(contentType)
            && contentType != "text/markdown"
            && contentType != "text/x-markdown"
            && contentType != "text/plain"
            && contentType != "application/octet-stream")
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid MIME type for Markdown file",
                Code = "INVALID_MIME_TYPE"
            });
        }

        var rateLimitResult = await CheckRateLimitAsync();
        if (rateLimitResult != null)
        {
            return rateLimitResult;
        }

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var markdownContent = await reader.ReadToEndAsync();

            var latexContent = ConvertMarkdownToLatex(markdownContent);

            await IncrementRateLimitAsync();

            var jobId = Guid.NewGuid();
            var filename = Path.GetFileNameWithoutExtension(file.FileName) + ".tex";
            await StoreConversionResultAsync(jobId, Encoding.UTF8.GetBytes(latexContent), "application/x-tex", filename);

            _logger.LogInformation(
                "[Convert] {ConversionType}: file={FileName}, size={SizeKB}KB, elapsed={ElapsedMs}ms",
                conversionType, file.FileName, file.Length / 1024, sw.ElapsedMilliseconds);

            return Ok(new ConversionResponse
            {
                JobId = jobId,
                Status = "completed",
                DownloadUrl = $"/api/convert/jobs/{jobId}/download",
                PollUrl = $"/api/convert/jobs/{jobId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Convert] {ConversionType} failed for {FileName}: {Message}",
                conversionType, file.FileName, ex.Message);
            return StatusCode(500, new ErrorResponse
            {
                Message = $"Conversion failed: {ex.Message}",
                Code = "INTERNAL_ERROR"
            });
        }
    }

    #region Distributed Cache Helpers

    private async Task StoreConversionResultAsync(Guid jobId, byte[] content, string contentType, string filename)
    {
        var result = new CachedConversionResult
        {
            Content = content,
            ContentType = contentType,
            Filename = filename
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(result);
        await _cache.SetAsync($"conversion:{jobId}", json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        });
    }

    private async Task<CachedRateLimit> GetOrCreateRateLimitEntryAsync(string identifier)
    {
        var key = $"ratelimit:{identifier}";
        var cached = await _cache.GetAsync(key);

        if (cached != null)
        {
            var entry = JsonSerializer.Deserialize<CachedRateLimit>(cached);
            if (entry != null && DateTime.UtcNow < entry.ResetTime)
                return entry;
        }

        // Create new entry — expires at midnight UTC
        var newEntry = new CachedRateLimit { Count = 0, ResetTime = DateTime.UtcNow.Date.AddDays(1) };
        var json = JsonSerializer.SerializeToUtf8Bytes(newEntry);
        await _cache.SetAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = newEntry.ResetTime
        });
        return newEntry;
    }

    private async Task<IActionResult?> CheckRateLimitAsync()
    {
        var identifier = GetRateLimitIdentifier();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var limit = isAuthenticated ? AuthenticatedLimitPerDay : AnonymousLimitPerDay;

        var entry = await GetOrCreateRateLimitEntryAsync(identifier);

        if (entry.Count >= limit)
        {
            Response.Headers["X-RateLimit-Limit"] = limit.ToString();
            Response.Headers["X-RateLimit-Remaining"] = "0";
            Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(entry.ResetTime).ToUnixTimeSeconds().ToString();

            return StatusCode(429, new ErrorResponse
            {
                Message = isAuthenticated
                    ? $"Rate limit exceeded. You have used all {limit} conversions for today."
                    : "Rate limit exceeded. Sign up for more conversions.",
                Code = "RATE_LIMIT_EXCEEDED"
            });
        }

        return null;
    }

    private async Task IncrementRateLimitAsync()
    {
        var identifier = GetRateLimitIdentifier();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var limit = isAuthenticated ? AuthenticatedLimitPerDay : AnonymousLimitPerDay;

        var key = $"ratelimit:{identifier}";
        var entry = await GetOrCreateRateLimitEntryAsync(identifier);
        entry.Count++;

        var json = JsonSerializer.SerializeToUtf8Bytes(entry);
        await _cache.SetAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = entry.ResetTime
        });

        var remaining = Math.Max(0, limit - entry.Count);
        Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(entry.ResetTime).ToUnixTimeSeconds().ToString();
    }

    private string GetRateLimitIdentifier()
    {
        var userId = User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var forwarded = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            ip = forwarded.Split(',').First().Trim();
        }

        return $"ip:{ip}";
    }

    private class CachedRateLimit
    {
        public int Count { get; set; }
        public DateTime ResetTime { get; set; }
    }

    private class CachedConversionResult
    {
        public byte[] Content { get; set; } = [];
        public string ContentType { get; set; } = "";
        public string Filename { get; set; } = "";
    }

    #endregion

    #region Rendering Methods

    private string RenderImportDocumentToLatex(ImportResult result)
    {
        var sb = new StringBuilder();
        var doc = result.IntermediateDocument ?? new ImportDocument { Title = "Converted Document" };

        sb.AppendLine(@"\documentclass[11pt,a4paper]{article}");
        sb.AppendLine(@"\usepackage[utf8]{inputenc}");
        sb.AppendLine(@"\usepackage{amsmath,amssymb,amsthm}");
        sb.AppendLine(@"\usepackage{graphicx}");
        sb.AppendLine(@"\usepackage{hyperref}");
        sb.AppendLine(@"\usepackage{listings}");
        sb.AppendLine(@"\usepackage{booktabs}");
        sb.AppendLine();
        sb.AppendLine($@"\title{{{EscapeLatex(doc.Title)}}}");
        if (!string.IsNullOrEmpty(doc.Metadata.Author))
        {
            sb.AppendLine($@"\author{{{EscapeLatex(doc.Metadata.Author)}}}");
        }
        sb.AppendLine(@"\date{\today}");
        sb.AppendLine();
        sb.AppendLine(@"\begin{document}");
        sb.AppendLine(@"\maketitle");
        sb.AppendLine();

        foreach (var element in doc.Elements)
        {
            sb.AppendLine(RenderElementToLatex(element));
        }

        sb.AppendLine(@"\end{document}");
        return sb.ToString();
    }

    private string RenderElementToLatex(ImportElement element)
    {
        return element switch
        {
            ImportHeading h => RenderHeadingToLatex(h),
            ImportParagraph p => RenderParagraphToLatex(p),
            ImportEquation eq => RenderEquationToLatex(eq),
            ImportCodeBlock cb => RenderCodeBlockToLatex(cb),
            ImportTable t => RenderTableToLatex(t),
            ImportImage img => RenderImageToLatex(img),
            ImportListItem li => RenderListItemToLatex(li),
            ImportLatexPassthrough lp => RenderLatexPassthroughToLatex(lp),
            ImportPageBreak => @"\newpage",
            _ => $"% Unsupported element type: {element.Type}"
        };
    }

    private string RenderLatexPassthroughToLatex(ImportLatexPassthrough passthrough)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(passthrough.Description))
        {
            sb.AppendLine($"% LaTeX Passthrough: {passthrough.Description}");
        }
        sb.AppendLine(passthrough.LatexCode);
        return sb.ToString();
    }

    private string RenderHeadingToLatex(ImportHeading heading)
    {
        var command = heading.Level switch
        {
            1 => "section",
            2 => "subsection",
            3 => "subsubsection",
            4 => "paragraph",
            5 => "subparagraph",
            _ => "section"
        };
        return $@"\{command}{{{EscapeLatex(heading.Text)}}}";
    }

    private string RenderParagraphToLatex(ImportParagraph para)
    {
        return EscapeLatex(para.Text) + "\n";
    }

    private string RenderEquationToLatex(ImportEquation eq)
    {
        var latex = eq.LatexContent ?? eq.OmmlXml;
        if (eq.IsInline)
        {
            return $"${latex}$";
        }
        return $@"\begin{{equation}}
{latex}
\end{{equation}}";
    }

    private string RenderCodeBlockToLatex(ImportCodeBlock code)
    {
        var langOption = !string.IsNullOrEmpty(code.Language) ? $"[language={code.Language}]" : "";
        return $@"\begin{{lstlisting}}{langOption}
{code.Text}
\end{{lstlisting}}";
    }

    private string RenderTableToLatex(ImportTable table)
    {
        if (table.Rows.Count == 0) return "";

        var sb = new StringBuilder();
        var colSpec = string.Join("", Enumerable.Repeat("c", table.ColumnCount));

        sb.AppendLine(@"\begin{table}[htbp]");
        sb.AppendLine(@"\centering");
        sb.AppendLine($@"\begin{{tabular}}{{{colSpec}}}");
        sb.AppendLine(@"\toprule");

        var isFirst = true;
        foreach (var row in table.Rows)
        {
            var cells = row.Select(c => EscapeLatex(c.Text));
            sb.AppendLine(string.Join(" & ", cells) + @" \\");
            if (isFirst && table.HasHeaderRow)
            {
                sb.AppendLine(@"\midrule");
                isFirst = false;
            }
        }

        sb.AppendLine(@"\bottomrule");
        sb.AppendLine(@"\end{tabular}");
        sb.AppendLine(@"\end{table}");
        return sb.ToString();
    }

    private string RenderImageToLatex(ImportImage img)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{figure}[htbp]");
        sb.AppendLine(@"\centering");
        sb.AppendLine($@"\includegraphics[width=0.8\textwidth]{{image-{img.Order}}}");
        if (!string.IsNullOrEmpty(img.AltText))
        {
            sb.AppendLine($@"\caption{{{EscapeLatex(img.AltText)}}}");
        }
        sb.AppendLine(@"\end{figure}");
        return sb.ToString();
    }

    private string RenderListItemToLatex(ImportListItem item)
    {
        return $@"\item {EscapeLatex(item.Text)}";
    }

    private string RenderImportDocumentToHtml(ImportResult result)
    {
        var sb = new StringBuilder();
        var doc = result.IntermediateDocument ?? new ImportDocument { Title = "Converted Document" };

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{System.Net.WebUtility.HtmlEncode(doc.Title)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: 'Times New Roman', serif; max-width: 800px; margin: 40px auto; padding: 20px; line-height: 1.6; }");
        sb.AppendLine("    h1 { font-size: 24pt; margin-top: 24pt; }");
        sb.AppendLine("    h2 { font-size: 18pt; margin-top: 18pt; }");
        sb.AppendLine("    h3 { font-size: 14pt; margin-top: 14pt; }");
        sb.AppendLine("    pre { background: #f5f5f5; padding: 12px; overflow-x: auto; font-family: 'Courier New', monospace; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; margin: 12px 0; }");
        sb.AppendLine("    th, td { border: 1px solid #000; padding: 8px; text-align: left; }");
        sb.AppendLine("    figure { text-align: center; margin: 20px 0; }");
        sb.AppendLine("    figcaption { font-style: italic; margin-top: 8px; }");
        sb.AppendLine("    .equation { text-align: center; margin: 16px 0; font-style: italic; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        if (!string.IsNullOrEmpty(doc.Title))
        {
            sb.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(doc.Title)}</h1>");
        }

        foreach (var element in doc.Elements)
        {
            sb.AppendLine(RenderElementToHtml(element));
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private string RenderElementToHtml(ImportElement element)
    {
        return element switch
        {
            ImportHeading h => $"<h{Math.Min(6, h.Level)}>{System.Net.WebUtility.HtmlEncode(h.Text)}</h{Math.Min(6, h.Level)}>",
            ImportParagraph p => $"<p>{System.Net.WebUtility.HtmlEncode(p.Text)}</p>",
            ImportEquation eq => eq.IsInline
                ? $"<span class=\"equation\">${eq.LatexContent ?? eq.OmmlXml}$</span>"
                : $"<div class=\"equation\">$${eq.LatexContent ?? eq.OmmlXml}$$</div>",
            ImportCodeBlock cb => $"<pre><code>{System.Net.WebUtility.HtmlEncode(cb.Text)}</code></pre>",
            ImportTable t => RenderTableToHtml(t),
            ImportImage img => RenderImageToHtml(img),
            ImportListItem li => $"<li>{System.Net.WebUtility.HtmlEncode(li.Text)}</li>",
            ImportLatexPassthrough lp => RenderLatexPassthroughToHtml(lp),
            ImportPageBreak => "<hr style=\"page-break-after: always;\">",
            _ => $"<!-- Unsupported element type: {element.Type} -->"
        };
    }

    private string RenderLatexPassthroughToHtml(ImportLatexPassthrough passthrough)
    {
        var desc = passthrough.Description ?? "Raw LaTeX";
        var preview = passthrough.LatexCode.Length > 100
            ? passthrough.LatexCode.Substring(0, 100) + "..."
            : passthrough.LatexCode;
        return $"<div class=\"latex-passthrough\" style=\"border: 1px dashed #f59e0b; padding: 12px; margin: 12px 0; background: #fffbeb;\">" +
               $"<div style=\"font-weight: bold; color: #b45309;\">⚡ {System.Net.WebUtility.HtmlEncode(desc)}</div>" +
               $"<pre style=\"font-size: 12px; color: #666; margin-top: 8px;\">{System.Net.WebUtility.HtmlEncode(preview)}</pre>" +
               $"</div>";
    }

    private string RenderTableToHtml(ImportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<table>");

        var isFirst = true;
        foreach (var row in table.Rows)
        {
            sb.Append("<tr>");
            var tag = isFirst && table.HasHeaderRow ? "th" : "td";
            foreach (var cell in row)
            {
                sb.Append($"<{tag}>{System.Net.WebUtility.HtmlEncode(cell.Text)}</{tag}>");
            }
            sb.AppendLine("</tr>");
            isFirst = false;
        }

        sb.AppendLine("</table>");
        return sb.ToString();
    }

    private string RenderImageToHtml(ImportImage img)
    {
        var sb = new StringBuilder();
        sb.Append("<figure>");

        if (img.Data.Length > 0)
        {
            var base64 = Convert.ToBase64String(img.Data);
            sb.Append($"<img src=\"data:{img.MimeType};base64,{base64}\" alt=\"{System.Net.WebUtility.HtmlEncode(img.AltText ?? "")}\"");
            if (img.WidthPixels.HasValue)
            {
                sb.Append($" width=\"{img.WidthPixels.Value}\"");
            }
            sb.Append(" />");
        }

        if (!string.IsNullOrEmpty(img.AltText))
        {
            sb.Append($"<figcaption>{System.Net.WebUtility.HtmlEncode(img.AltText)}</figcaption>");
        }

        sb.Append("</figure>");
        return sb.ToString();
    }

    private string RenderImportDocumentToMarkdown(ImportResult result)
    {
        var sb = new StringBuilder();
        var doc = result.IntermediateDocument ?? new ImportDocument { Title = "Converted Document" };

        if (!string.IsNullOrEmpty(doc.Title))
        {
            sb.AppendLine($"# {doc.Title}");
            sb.AppendLine();
        }

        foreach (var element in doc.Elements)
        {
            sb.AppendLine(RenderElementToMarkdown(element));
        }

        return sb.ToString();
    }

    private string RenderElementToMarkdown(ImportElement element)
    {
        return element switch
        {
            ImportHeading h => new string('#', h.Level) + " " + h.Text + "\n",
            ImportParagraph p => p.Text + "\n",
            ImportEquation eq => eq.IsInline
                ? $"${eq.LatexContent ?? eq.OmmlXml}$"
                : $"$$\n{eq.LatexContent ?? eq.OmmlXml}\n$$\n",
            ImportCodeBlock cb => $"```{cb.Language ?? ""}\n{cb.Text}\n```\n",
            ImportTable t => RenderTableToMarkdown(t),
            ImportImage img => $"![{img.AltText ?? "Image"}](image-{img.Order})\n",
            ImportListItem li => (li.IsNumbered ? "1. " : "- ") + li.Text,
            ImportLatexPassthrough lp => RenderLatexPassthroughToMarkdown(lp),
            ImportPageBreak => "---\n",
            _ => ""
        };
    }

    private string RenderLatexPassthroughToMarkdown(ImportLatexPassthrough passthrough)
    {
        var desc = passthrough.Description ?? "Raw LaTeX";
        return $"```latex\n% {desc}\n{passthrough.LatexCode}\n```\n";
    }

    private string RenderTableToMarkdown(ImportTable table)
    {
        if (table.Rows.Count == 0) return "";

        var sb = new StringBuilder();

        if (table.Rows.Count > 0)
        {
            sb.AppendLine("| " + string.Join(" | ", table.Rows[0].Select(c => c.Text)) + " |");
            sb.AppendLine("| " + string.Join(" | ", table.Rows[0].Select(_ => "---")) + " |");
        }

        foreach (var row in table.Rows.Skip(table.HasHeaderRow ? 1 : 0))
        {
            sb.AppendLine("| " + string.Join(" | ", row.Select(c => c.Text)) + " |");
        }

        return sb.ToString();
    }

    private ExportDocument ParseLatexToExportDocument(string latexContent)
    {
        var doc = new ExportDocument { Title = "Converted Document" };
        var lines = latexContent.Split('\n');
        var inDocument = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(@"\title{"))
            {
                var titleMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"\\title\{(.+?)\}");
                if (titleMatch.Success)
                {
                    doc.Title = titleMatch.Groups[1].Value;
                }
                continue;
            }

            if (trimmed == @"\begin{document}")
            {
                inDocument = true;
                continue;
            }
            if (trimmed == @"\end{document}")
            {
                break;
            }

            if (!inDocument) continue;

            if (trimmed.StartsWith(@"\maketitle")) continue;
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith('%')) continue;

            var sectionMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"\\(section|subsection|subsubsection|paragraph)\{(.+?)\}");
            if (sectionMatch.Success)
            {
                var level = sectionMatch.Groups[1].Value switch
                {
                    "section" => 1,
                    "subsection" => 2,
                    "subsubsection" => 3,
                    "paragraph" => 4,
                    _ => 1
                };
                doc.Blocks.Add(new ExportBlock
                {
                    Type = "heading",
                    Content = new ExportBlockContent { Text = sectionMatch.Groups[2].Value, Level = level }
                });
                continue;
            }

            if (!trimmed.StartsWith('\\') && !trimmed.StartsWith('{'))
            {
                doc.Blocks.Add(new ExportBlock
                {
                    Type = "paragraph",
                    Content = new ExportBlockContent { Text = trimmed }
                });
            }
        }

        return doc;
    }

    private string ConvertMarkdownToLatex(string markdown)
    {
        var sb = new StringBuilder();

        sb.AppendLine(@"\documentclass[11pt,a4paper]{article}");
        sb.AppendLine(@"\usepackage[utf8]{inputenc}");
        sb.AppendLine(@"\usepackage{amsmath,amssymb}");
        sb.AppendLine(@"\usepackage{graphicx}");
        sb.AppendLine(@"\usepackage{hyperref}");
        sb.AppendLine(@"\usepackage{listings}");
        sb.AppendLine();
        sb.AppendLine(@"\begin{document}");
        sb.AppendLine();

        var lines = markdown.Split('\n');
        var inCodeBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    sb.AppendLine(@"\end{lstlisting}");
                    inCodeBlock = false;
                }
                else
                {
                    var lang = trimmed.Length > 3 ? trimmed[3..] : "";
                    var langOption = !string.IsNullOrEmpty(lang) ? $"[language={lang}]" : "";
                    sb.AppendLine($@"\begin{{lstlisting}}{langOption}");
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine(line);
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                var text = trimmed[level..].Trim();
                var command = level switch
                {
                    1 => "section",
                    2 => "subsection",
                    3 => "subsubsection",
                    4 => "paragraph",
                    _ => "paragraph"
                };
                sb.AppendLine($@"\{command}{{{EscapeLatex(text)}}}");
                continue;
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                sb.AppendLine($@"\item {EscapeLatex(trimmed[2..])}");
                continue;
            }

            if (!string.IsNullOrEmpty(trimmed))
            {
                sb.AppendLine(EscapeLatex(trimmed));
                sb.AppendLine();
            }
        }

        sb.AppendLine(@"\end{document}");
        return sb.ToString();
    }

    private static string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        return text
            .Replace("\\", "\\textbackslash{}")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("$", "\\$")
            .Replace("&", "\\&")
            .Replace("#", "\\#")
            .Replace("^", "\\textasciicircum{}")
            .Replace("_", "\\_")
            .Replace("~", "\\textasciitilde{}")
            .Replace("%", "\\%");
    }

    #endregion

    #region LaTeX Text to Blocks

    /// <summary>
    /// Parse raw LaTeX text and return structured editor blocks.
    /// </summary>
    [HttpPost("latex-to-blocks")]
    [Authorize]
    [ProducesResponseType(typeof(LatexToBlocksResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LatexToBlocks([FromBody] LatexToBlocksRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Latex))
            return BadRequest(new ErrorResponse { Message = "LaTeX content is required.", Code = "EMPTY_INPUT" });

        if (request.Latex.Length > MaxLatexTextLength)
            return BadRequest(new ErrorResponse { Message = $"LaTeX content must be at most {MaxLatexTextLength} characters.", Code = "INPUT_TOO_LARGE" });

        try
        {
            var importDoc = await _latexParser.ParseTextAsync(request.Latex);

            var blocks = new List<LatexBlockDto>();
            var warnings = new List<string>();

            // If the preamble captured CV-style personal info, emit a
            // personalInfo block at the top of the document so the CV's
            // identity surface survives into the editor.
            var meta = importDoc.Metadata;
            if (!string.IsNullOrWhiteSpace(meta.PersonName)
                || !string.IsNullOrWhiteSpace(meta.Email)
                || meta.Phones.Count > 0
                || meta.Socials.Count > 0
                || !string.IsNullOrWhiteSpace(meta.Homepage))
            {
                blocks.Add(new LatexBlockDto("personalInfo", new
                {
                    name = meta.PersonName ?? "",
                    email = meta.Email ?? "",
                    phones = meta.Phones.Select(p => new { kind = p.Kind, number = p.Number }).ToList(),
                    homepage = meta.Homepage ?? "",
                    socials = meta.Socials.Select(s => new { network = s.Network, handle = s.Handle }).ToList(),
                    extra = meta.ExtraInfo ?? ""
                }));
            }

            // Preamble photo → dedicated photo block so CV avatar geometry
            // isn't lost inside a generic figure block.
            if (!string.IsNullOrWhiteSpace(meta.PhotoFilename))
            {
                blocks.Add(new LatexBlockDto("photo", new
                {
                    src = meta.PhotoFilename,
                    alt = meta.PersonName ?? "",
                    shape = "square",
                    size = 64,
                    position = "right",
                    border = 0
                }));
            }

            // Buffer for grouping consecutive ImportListItem / ImportBibliographyEntry into a single block.
            var listBuffer = new List<ImportListItem>();
            var bibBuffer = new List<ImportBibliographyEntry>();

            void FlushList()
            {
                if (listBuffer.Count == 0) return;
                var ordered = listBuffer[0].IsNumbered;
                blocks.Add(new LatexBlockDto("list", new
                {
                    ordered,
                    items = listBuffer.Select(li => new
                    {
                        text = ConvertLatexFormattingToMarkdown(li.Text),
                        marker = li.ListMarker,
                    }).ToList(),
                }));
                listBuffer.Clear();
            }

            void FlushBib()
            {
                if (bibBuffer.Count == 0) return;
                blocks.Add(new LatexBlockDto("bibliography", new
                {
                    entries = bibBuffer.Select(e => new
                    {
                        citeKey = e.ReferenceLabel,
                        text = ConvertLatexFormattingToMarkdown(e.Text),
                    }).ToList(),
                }));
                bibBuffer.Clear();
            }

            foreach (var element in importDoc.Elements.OrderBy(e => e.Order))
            {
                // Flush buffers when we see a different element type.
                if (element is not ImportListItem) FlushList();
                if (element is not ImportBibliographyEntry) FlushBib();

                switch (element)
                {
                    case ImportHeading h:
                        blocks.Add(new LatexBlockDto("heading", new { text = ConvertLatexFormattingToMarkdown(h.Text), level = h.Level }));
                        break;
                    case ImportParagraph p:
                        blocks.Add(new LatexBlockDto("paragraph", new { text = ConvertLatexFormattingToMarkdown(p.Text) }));
                        break;
                    case ImportEquation eq:
                        blocks.Add(new LatexBlockDto("equation", new { latex = eq.LatexContent, equationMode = eq.IsInline ? "inline" : "display" }));
                        break;
                    case ImportCodeBlock cb:
                        blocks.Add(new LatexBlockDto("code", new { code = cb.Text, language = cb.Language ?? "" }));
                        break;
                    case ImportTable t:
                        var headers = t.HasHeaderRow && t.Rows.Count > 0
                            ? t.Rows[0].Select(c => c.Text).ToList()
                            : t.Rows.FirstOrDefault()?.Select(c => c.Text).ToList() ?? [];
                        var rows = t.HasHeaderRow && t.Rows.Count > 1
                            ? t.Rows.Skip(1).Select(r => r.Select(c => c.Text).ToList()).ToList()
                            : t.Rows.Count > 1
                                ? t.Rows.Skip(1).Select(r => r.Select(c => c.Text).ToList()).ToList()
                                : [];
                        blocks.Add(new LatexBlockDto("table", new { headers, rows, span = t.Span }));
                        break;
                    case ImportImage img:
                        // P0-4: propagate the actual filename to the editor block so the user
                        // doesn't have to re-attach every figure.
                        blocks.Add(new LatexBlockDto("figure", new
                        {
                            caption = img.AltText ?? "",
                            src = img.Filename ?? "",
                            alt = img.AltText ?? "",
                            span = img.Span,
                        }));
                        break;
                    case ImportListItem li:
                        // Collect into a single list block — flushed on element-type change.
                        if (listBuffer.Count > 0 && listBuffer[0].IsNumbered != li.IsNumbered)
                        {
                            FlushList();
                        }
                        listBuffer.Add(li);
                        break;
                    case ImportTheorem th:
                        blocks.Add(new LatexBlockDto("theorem", new
                        {
                            text = ConvertLatexFormattingToMarkdown(th.Text),
                            kind = th.EnvironmentType.ToString().ToLowerInvariant(),
                            title = th.Title ?? "",
                            label = th.Label ?? "",
                        }));
                        break;
                    case ImportAlgorithm algo:
                        blocks.Add(new LatexBlockDto("algorithm", new
                        {
                            caption = algo.Caption ?? "",
                            label = algo.Label ?? "",
                            // Keep the flat string for backwards compat, but
                            // also emit the structured lines[] so the editor
                            // can render each statement as its own row and
                            // validation can check \IF/\ENDIF pairing.
                            code = algo.Code,
                            lineNumbers = algo.LineNumbers,
                            lines = algo.Lines.Select(l => new { kind = l.Kind, text = l.Text }).ToList(),
                        }));
                        break;
                    case ImportAbstract ab:
                        blocks.Add(new LatexBlockDto("abstract", new { text = ConvertLatexFormattingToMarkdown(ab.Text) }));
                        break;
                    case ImportBibliographyEntry be:
                        bibBuffer.Add(be);
                        break;
                    case ImportBlockquote bq:
                        blocks.Add(new LatexBlockDto("blockquote", new { text = ConvertLatexFormattingToMarkdown(bq.Text) }));
                        break;
                    case ImportPageBreak:
                        blocks.Add(new LatexBlockDto("pageBreak", new { }));
                        break;
                    case ImportLatexPassthrough lp:
                        // Preserve raw LaTeX (TikZ, custom envs, etc.) as an embed block —
                        // the editor's escape hatch for verbatim LaTeX that survives export unchanged.
                        blocks.Add(new LatexBlockDto("embed", new
                        {
                            code = lp.LatexCode,
                            description = lp.Description ?? "",
                        }));
                        break;
                    default:
                        warnings.Add($"Skipped unsupported element type: {element.GetType().Name}");
                        break;
                }
            }

            // Final flush for any trailing buffered items.
            FlushList();
            FlushBib();

            foreach (var w in importDoc.Warnings)
                warnings.Add(w.Message);

            // Surface the preamble metadata so the editor can restore the user's
            // original document class / package list / bibliography style on save.
            LatexPreambleDto? preamble = null;
            if (!string.IsNullOrEmpty(importDoc.Metadata.DocumentClass)
                || importDoc.Metadata.Packages.Count > 0
                || !string.IsNullOrEmpty(importDoc.Metadata.Date)
                || !string.IsNullOrEmpty(importDoc.Metadata.BibliographyStyle)
                || !string.IsNullOrEmpty(importDoc.Metadata.Author)
                || !string.IsNullOrEmpty(importDoc.Metadata.GeometryOptions)
                || importDoc.Metadata.UsesTitlesec
                || !string.IsNullOrEmpty(importDoc.Metadata.Language)
                || importDoc.Metadata.CitedKeys.Count > 0
                || importDoc.Metadata.ReferencedLabels.Count > 0
                || !string.IsNullOrEmpty(importDoc.Metadata.LineSpacing)
                || importDoc.Metadata.UsesFancyhdr)
            {
                preamble = new LatexPreambleDto
                {
                    DocumentClass = importDoc.Metadata.DocumentClass,
                    DocumentClassOptions = importDoc.Metadata.DocumentClassOptions,
                    Packages = importDoc.Metadata.Packages
                        .Select(p => new LatexPackageDto { Name = p.Name, Options = p.Options })
                        .ToList(),
                    Date = importDoc.Metadata.Date,
                    BibliographyStyle = importDoc.Metadata.BibliographyStyle,
                    Author = importDoc.Metadata.Author,
                    GeometryOptions = importDoc.Metadata.GeometryOptions,
                    UsesTitlesec = importDoc.Metadata.UsesTitlesec,
                    Language = importDoc.Metadata.Language,
                    CitedKeys = importDoc.Metadata.CitedKeys,
                    ReferencedLabels = importDoc.Metadata.ReferencedLabels,
                    LineSpacing = importDoc.Metadata.LineSpacing,
                    UsesFancyhdr = importDoc.Metadata.UsesFancyhdr,
                    FancyhdrSource = importDoc.Metadata.FancyhdrSource,
                };
            }

            return Ok(new LatexToBlocksResponse
            {
                Blocks = blocks,
                Title = importDoc.Title,
                Warnings = warnings.Count > 0 ? warnings : null,
                Preamble = preamble,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LaTeX text");
            return BadRequest(new ErrorResponse { Message = "Failed to parse LaTeX content.", Code = "PARSE_ERROR" });
        }
    }

    #endregion

    /// <summary>
    /// Converts LaTeX inline formatting commands to Markdown equivalents
    /// so the editor's rich-text renderer can display them correctly.
    /// </summary>
    private static string ConvertLatexFormattingToMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Inline formatting commands — the output format must match what the
        // editor's content-converter.ts parseInlineContent expects:
        //   **bold** → bold mark
        //   *italic* → italic mark (single asterisk, NOT underscore)
        //   `code` → code mark
        //   __underline__ → underline mark (double underscore)
        // Prior versions emitted `_italic_` and `<u>...</u>` which the client
        // parser didn't recognize — those formats are no longer produced.

        // \textbf{...} → **...**
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\textbf\{([^}]*)\}", "**$1**");
        // \textit{...} → *...* (single asterisk)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\textit\{([^}]*)\}", "*$1*");
        // \emph{...} → *...* (single asterisk — emph maps to italic)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\emph\{([^}]*)\}", "*$1*");
        // \texttt{...} → `...`
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\texttt\{([^}]*)\}", "`$1`");
        // \underline{...} → __...__ (double underscore — client's underline mark)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\underline\{([^}]*)\}", "__$1__");
        // \textsc{...} → just the text (no markdown equivalent; small caps not in our mark set)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\textsc\{([^}]*)\}", "$1");
        // \textrm{...} → just the text
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\textrm\{([^}]*)\}", "$1");
        // \textsf{...} → just the text
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\textsf\{([^}]*)\}", "$1");

        // Typographic quotes — convert LaTeX backtick/apostrophe syntax to
        // Unicode curly quotes so they don't trip up the client's inline-code
        // markdown parser (which treats `text` as code).
        //   ``double-open''  → " "
        //   `single-open'    → ' '
        text = System.Text.RegularExpressions.Regex.Replace(text, @"``([^`']*?)''", "\u201C$1\u201D");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<![`])`([^`']*?)'(?!')", "\u2018$1\u2019");

        // Non-breaking space (~) → regular space
        text = text.Replace("~", " ");
        // Thin space (\,) → regular space (not empty string — stripping collapses words)
        text = text.Replace("\\,", " ");
        // Backslash-space (\ ) used for non-breaking inter-word spacing → regular space
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\\ (?=\S)", " ");

        return text;
    }
}

public record LatexToBlocksRequest(string Latex);

public record LatexBlockDto(string Type, object Content);

public class LatexToBlocksResponse
{
    public List<LatexBlockDto> Blocks { get; set; } = [];
    public string? Title { get; set; }
    public List<string>? Warnings { get; set; }
    public LatexPreambleDto? Preamble { get; set; }
}

/// <summary>
/// Preamble metadata extracted from the LaTeX source so the editor can
/// restore document class, packages, and bibliography style on save.
/// </summary>
public class LatexPreambleDto
{
    public string? DocumentClass { get; set; }
    public string? DocumentClassOptions { get; set; }
    public List<LatexPackageDto> Packages { get; set; } = [];
    public string? Date { get; set; }
    public string? BibliographyStyle { get; set; }
    public string? Author { get; set; }
    public string? GeometryOptions { get; set; }
    public bool UsesTitlesec { get; set; }
    public string? Language { get; set; }
    public List<string> CitedKeys { get; set; } = [];
    public List<string> ReferencedLabels { get; set; } = [];
    public string? LineSpacing { get; set; }
    public bool UsesFancyhdr { get; set; }
    public string? FancyhdrSource { get; set; }
}

public class LatexPackageDto
{
    public string Name { get; set; } = string.Empty;
    public string? Options { get; set; }
}

public class ConversionResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "pending";
    public string? DownloadUrl { get; set; }
    public string? PollUrl { get; set; }
}

public class JobStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "";
    public int Progress { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Error { get; set; }
}

public class QuotaResponse
{
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTime ResetsAt { get; set; }
    public bool IsAuthenticated { get; set; }
}

public class ErrorResponse
{
    public string Message { get; set; } = "";
    public string Code { get; set; } = "";
}
