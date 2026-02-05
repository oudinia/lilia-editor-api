using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly ILogger<ConvertController> _logger;

    // Simple in-memory rate limiting (should be replaced with Redis in production)
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();
    private static readonly ConcurrentDictionary<Guid, ConversionResult> _conversionResults = new();

    private const int AnonymousLimitPerDay = 3;
    private const int AuthenticatedLimitPerDay = 10;
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20MB

    public ConvertController(
        IDocxImportService docxImportService,
        IDocxExportService docxExportService,
        ILogger<ConvertController> logger)
    {
        _docxImportService = docxImportService;
        _docxExportService = docxExportService;
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
        // PDF export returns HTML that can be printed to PDF via browser
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
    public IActionResult GetJobStatus(Guid jobId)
    {
        if (_conversionResults.TryGetValue(jobId, out var result))
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
    public IActionResult DownloadResult(Guid jobId)
    {
        if (_conversionResults.TryGetValue(jobId, out var result))
        {
            // Remove from cache after download
            _conversionResults.TryRemove(jobId, out _);
            return File(result.Content, result.ContentType, result.Filename);
        }

        return NotFound(new ErrorResponse { Message = "Download not available", Code = "NOT_FOUND" });
    }

    /// <summary>
    /// Get remaining conversion quota for the current user/IP.
    /// </summary>
    [HttpGet("quota")]
    [ProducesResponseType(typeof(QuotaResponse), StatusCodes.Status200OK)]
    public IActionResult GetQuota()
    {
        var identifier = GetRateLimitIdentifier();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var limit = isAuthenticated ? AuthenticatedLimitPerDay : AnonymousLimitPerDay;

        var entry = GetOrCreateRateLimitEntry(identifier);
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

        // Validate file
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

        // Check rate limit
        var rateLimitResult = CheckRateLimit();
        if (rateLimitResult != null)
        {
            return rateLimitResult;
        }

        try
        {
            // Save uploaded file to temp location
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Parse DOCX using local service
                var importResult = await _docxImportService.ImportAsync(tempPath);

                if (!importResult.Success)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = importResult.ErrorMessage ?? "Failed to parse DOCX",
                        Code = "PARSE_ERROR"
                    });
                }

                // Convert to target format
                string content;
                string contentType;
                string fileExtension;

                switch (targetFormat)
                {
                    case "latex":
                        content = RenderImportDocumentToLatex(importResult);
                        contentType = "application/x-tex";
                        fileExtension = ".tex";
                        break;
                    case "html":
                        content = RenderImportDocumentToHtml(importResult);
                        contentType = "text/html";
                        fileExtension = ".html";
                        break;
                    case "markdown":
                        content = RenderImportDocumentToMarkdown(importResult);
                        contentType = "text/markdown";
                        fileExtension = ".md";
                        break;
                    default:
                        return BadRequest(new ErrorResponse { Message = $"Unsupported target format: {targetFormat}", Code = "INVALID_FORMAT" });
                }

                // Increment rate limit counter
                IncrementRateLimit();

                // Store result for download
                var jobId = Guid.NewGuid();
                var filename = Path.GetFileNameWithoutExtension(file.FileName) + fileExtension;
                _conversionResults[jobId] = new ConversionResult
                {
                    Content = Encoding.UTF8.GetBytes(content),
                    ContentType = contentType,
                    Filename = filename
                };

                // Clean up old results (older than 1 hour)
                CleanupOldResults();

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
                // Clean up temp file
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

        // Validate file
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

        // Check rate limit
        var rateLimitResult = CheckRateLimit();
        if (rateLimitResult != null)
        {
            return rateLimitResult;
        }

        try
        {
            // Read LaTeX content
            using var reader = new StreamReader(file.OpenReadStream());
            var latexContent = await reader.ReadToEndAsync();

            // Convert LaTeX to ExportDocument structure
            var exportDoc = ParseLatexToExportDocument(latexContent);

            // Export to DOCX
            var docxBytes = await _docxExportService.ExportAsync(exportDoc);

            // Increment rate limit counter
            IncrementRateLimit();

            // Store result for download
            var jobId = Guid.NewGuid();
            var filename = Path.GetFileNameWithoutExtension(file.FileName) + ".docx";
            _conversionResults[jobId] = new ConversionResult
            {
                Content = docxBytes,
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Filename = filename
            };

            CleanupOldResults();

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

        // Validate file
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

        // Check rate limit
        var rateLimitResult = CheckRateLimit();
        if (rateLimitResult != null)
        {
            return rateLimitResult;
        }

        try
        {
            // Read Markdown content
            using var reader = new StreamReader(file.OpenReadStream());
            var markdownContent = await reader.ReadToEndAsync();

            // Convert Markdown to LaTeX
            var latexContent = ConvertMarkdownToLatex(markdownContent);

            // Increment rate limit counter
            IncrementRateLimit();

            // Store result for download
            var jobId = Guid.NewGuid();
            var filename = Path.GetFileNameWithoutExtension(file.FileName) + ".tex";
            _conversionResults[jobId] = new ConversionResult
            {
                Content = Encoding.UTF8.GetBytes(latexContent),
                ContentType = "application/x-tex",
                Filename = filename
            };

            CleanupOldResults();

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

    #region Rendering Methods

    private string RenderImportDocumentToLatex(ImportResult result)
    {
        var sb = new StringBuilder();
        var doc = result.IntermediateDocument ?? new ImportDocument { Title = "Converted Document" };

        // Preamble
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

        // Elements
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
            ImportPageBreak => @"\newpage",
            _ => $"% Unsupported element type: {element.Type}"
        };
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
            ImportPageBreak => "<hr style=\"page-break-after: always;\">",
            _ => $"<!-- Unsupported element type: {element.Type} -->"
        };
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
            ImportPageBreak => "---\n",
            _ => ""
        };
    }

    private string RenderTableToMarkdown(ImportTable table)
    {
        if (table.Rows.Count == 0) return "";

        var sb = new StringBuilder();

        // Header row
        if (table.Rows.Count > 0)
        {
            sb.AppendLine("| " + string.Join(" | ", table.Rows[0].Select(c => c.Text)) + " |");
            sb.AppendLine("| " + string.Join(" | ", table.Rows[0].Select(_ => "---")) + " |");
        }

        // Data rows
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

            // Extract title
            if (trimmed.StartsWith(@"\title{"))
            {
                var titleMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"\\title\{(.+?)\}");
                if (titleMatch.Success)
                {
                    doc.Title = titleMatch.Groups[1].Value;
                }
                continue;
            }

            // Track document environment
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

            // Skip common commands
            if (trimmed.StartsWith(@"\maketitle")) continue;
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith('%')) continue;

            // Parse sections
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

            // Regular text becomes paragraph
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

        // Preamble
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

            // Code blocks
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

            // Headers
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

            // Lists
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                sb.AppendLine($@"\item {EscapeLatex(trimmed[2..])}");
                continue;
            }

            // Regular paragraph
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

    #region Rate Limiting

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

    private RateLimitEntry GetOrCreateRateLimitEntry(string identifier)
    {
        var now = DateTime.UtcNow;
        var resetTime = now.Date.AddDays(1);

        return _rateLimits.AddOrUpdate(
            identifier,
            _ => new RateLimitEntry { Count = 0, ResetTime = resetTime },
            (_, existing) =>
            {
                if (now >= existing.ResetTime)
                {
                    return new RateLimitEntry { Count = 0, ResetTime = resetTime };
                }
                return existing;
            });
    }

    private IActionResult? CheckRateLimit()
    {
        var identifier = GetRateLimitIdentifier();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var limit = isAuthenticated ? AuthenticatedLimitPerDay : AnonymousLimitPerDay;

        var entry = GetOrCreateRateLimitEntry(identifier);

        if (entry.Count >= limit)
        {
            Response.Headers["X-RateLimit-Limit"] = limit.ToString();
            Response.Headers["X-RateLimit-Remaining"] = "0";
            Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(entry.ResetTime).ToUnixTimeSeconds().ToString();

            return StatusCode(429, new ErrorResponse
            {
                Message = isAuthenticated
                    ? $"Rate limit exceeded. You have used all {limit} conversions for today."
                    : $"Rate limit exceeded. Sign up for more conversions.",
                Code = "RATE_LIMIT_EXCEEDED"
            });
        }

        return null;
    }

    private void IncrementRateLimit()
    {
        var identifier = GetRateLimitIdentifier();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var limit = isAuthenticated ? AuthenticatedLimitPerDay : AnonymousLimitPerDay;

        _rateLimits.AddOrUpdate(
            identifier,
            _ => new RateLimitEntry { Count = 1, ResetTime = DateTime.UtcNow.Date.AddDays(1) },
            (_, existing) =>
            {
                existing.Count++;
                return existing;
            });

        var entry = _rateLimits[identifier];
        var remaining = Math.Max(0, limit - entry.Count);

        Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(entry.ResetTime).ToUnixTimeSeconds().ToString();
    }

    private void CleanupOldResults()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var toRemove = _conversionResults
            .Where(kv => kv.Value.CreatedAt < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _conversionResults.TryRemove(key, out _);
        }
    }

    #endregion

    #region Helper Classes

    private class RateLimitEntry
    {
        public int Count { get; set; }
        public DateTime ResetTime { get; set; }
    }

    private class ConversionResult
    {
        public byte[] Content { get; set; } = [];
        public string ContentType { get; set; } = "";
        public string Filename { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion
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
