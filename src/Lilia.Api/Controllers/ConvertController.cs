using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConvertController> _logger;

    // Simple in-memory rate limiting (should be replaced with Redis in production)
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();

    private const int AnonymousLimitPerDay = 3;
    private const int AuthenticatedLimitPerDay = 10;
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20MB

    public ConvertController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ConvertController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Convert DOCX to LaTeX format.
    /// </summary>
    [HttpPost("docx-to-latex")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertDocxToLatex(
        [FromForm] IFormFile file,
        [FromForm] string? options = null)
    {
        return await HandleConversion(file, "docx", "latex", options);
    }

    /// <summary>
    /// Convert DOCX to PDF format.
    /// </summary>
    [HttpPost("docx-to-pdf")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertDocxToPdf([FromForm] IFormFile file)
    {
        return await HandleConversion(file, "docx", "pdf", null);
    }

    /// <summary>
    /// Convert LaTeX to PDF format.
    /// </summary>
    [HttpPost("latex-to-pdf")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertLatexToPdf([FromForm] IFormFile file)
    {
        return await HandleConversion(file, "latex", "pdf", null);
    }

    /// <summary>
    /// Convert LaTeX to DOCX format.
    /// </summary>
    [HttpPost("latex-to-docx")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertLatexToDocx([FromForm] IFormFile file)
    {
        return await HandleConversion(file, "latex", "docx", null);
    }

    /// <summary>
    /// Convert Markdown to LaTeX format.
    /// </summary>
    [HttpPost("markdown-to-latex")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ConversionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConvertMarkdownToLatex(
        [FromForm] IFormFile file,
        [FromForm] string? options = null)
    {
        return await HandleConversion(file, "markdown", "latex", options);
    }

    /// <summary>
    /// Get conversion job status.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(JobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatus(Guid jobId)
    {
        var sw = Stopwatch.StartNew();

        // Check for mock mode
        var useMockMode = _configuration.GetValue<bool>("DocxApi:MockMode", false);
        if (useMockMode)
        {
            return Ok(new JobStatusResponse
            {
                JobId = jobId,
                Status = "COMPLETED",
                Progress = 100,
                DownloadUrl = $"/api/convert/jobs/{jobId}/download"
            });
        }

        try
        {
            var client = _httpClientFactory.CreateClient("DocxApi");
            var response = await client.GetAsync($"/api/v1/jobs/{jobId}");

            if (!response.IsSuccessStatusCode)
            {
                return NotFound(new ErrorResponse { Message = "Job not found", Code = "JOB_NOT_FOUND" });
            }

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[Convert] GET job {JobId}: {ElapsedMs}ms", jobId, sw.ElapsedMilliseconds);

            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Convert] Failed to get job status for {JobId}", jobId);
            return StatusCode(500, new ErrorResponse { Message = "Failed to get job status", Code = "INTERNAL_ERROR" });
        }
    }

    /// <summary>
    /// Download conversion result.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadResult(Guid jobId)
    {
        // Check for mock mode
        var useMockMode = _configuration.GetValue<bool>("DocxApi:MockMode", false);
        if (useMockMode)
        {
            // Return a sample LaTeX file
            var sampleLatex = @"\documentclass[11pt,a4paper]{article}
\usepackage[utf8]{inputenc}
\usepackage{amsmath,amssymb}

\title{Converted Document}
\author{Lilia Converter}
\date{\today}

\begin{document}
\maketitle

\section{Introduction}
This is a sample converted document. In production, this would contain the actual converted content from your uploaded file.

\section{Sample Content}
Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.

\subsection{Mathematics}
Here is a sample equation:
\begin{equation}
E = mc^2
\end{equation}

\end{document}";

            var bytes = System.Text.Encoding.UTF8.GetBytes(sampleLatex);
            return File(bytes, "text/x-tex", "converted-document.tex");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("DocxApi");
            var response = await client.GetAsync($"/api/v1/jobs/{jobId}/download");

            if (!response.IsSuccessStatusCode)
            {
                return NotFound(new ErrorResponse { Message = "Download not available", Code = "NOT_FOUND" });
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "output";

            return File(bytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Convert] Failed to download result for {JobId}", jobId);
            return StatusCode(500, new ErrorResponse { Message = "Failed to download", Code = "INTERNAL_ERROR" });
        }
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

    private async Task<IActionResult> HandleConversion(
        IFormFile? file,
        string sourceFormat,
        string targetFormat,
        string? options)
    {
        var sw = Stopwatch.StartNew();
        var conversionType = $"{sourceFormat}-to-{targetFormat}";

        // Validate file
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Message = "No file provided", Code = "NO_FILE" });
        }

        // Check file extension
        var validExtensions = GetValidExtensions(sourceFormat);
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (!validExtensions.Contains(extension))
        {
            return BadRequest(new ErrorResponse
            {
                Message = $"Invalid file type. Expected: {string.Join(", ", validExtensions)}",
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
            // Check if DocxApi is configured
            var docxApiBaseUrl = _configuration["DocxApi:BaseUrl"];

            // Development mock mode - if DocxApi not configured, return mock response
            var useMockMode = _configuration.GetValue<bool>("DocxApi:MockMode", false);
            if (string.IsNullOrEmpty(docxApiBaseUrl) || useMockMode)
            {
                _logger.LogInformation("[Convert] Using mock mode for {ConversionType}", conversionType);

                // Increment rate limit even in mock mode
                IncrementRateLimit();

                // Return a mock job ID
                var mockJobId = Guid.NewGuid();
                return Ok(new ConversionResponse
                {
                    JobId = mockJobId,
                    Status = "completed",
                    DownloadUrl = $"/api/convert/jobs/{mockJobId}/download",
                    PollUrl = $"/api/convert/jobs/{mockJobId}"
                });
            }

            // Create job via docx-api
            var client = _httpClientFactory.CreateClient("DocxApi");

            using var content = new MultipartFormDataContent();

            // Read file into memory first to avoid stream disposal issues
            using var memoryStream = new MemoryStream();
            await file.OpenReadStream().CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var fileContent = new ByteArrayContent(memoryStream.ToArray());
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "file", file.FileName);

            if (!string.IsNullOrEmpty(options))
            {
                content.Add(new StringContent(options), "options");
            }

            // Route to appropriate endpoint based on conversion type
            var endpoint = GetConversionEndpoint(sourceFormat, targetFormat);

            _logger.LogInformation("[Convert] Calling DocxApi: {BaseUrl}{Endpoint}", docxApiBaseUrl, endpoint);

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(endpoint, content);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[Convert] Failed to connect to DocxApi at {BaseUrl}", docxApiBaseUrl);
                return StatusCode(503, new ErrorResponse
                {
                    Message = "Conversion service is unavailable. Please try again later.",
                    Code = "SERVICE_UNAVAILABLE"
                });
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[Convert] {ConversionType} failed: {StatusCode} - {Error}",
                    conversionType, response.StatusCode, errorContent);

                return StatusCode((int)response.StatusCode, new ErrorResponse
                {
                    Message = "Conversion failed",
                    Code = "CONVERSION_ERROR"
                });
            }

            // Increment rate limit counter
            IncrementRateLimit();

            var result = await response.Content.ReadAsStringAsync();

            _logger.LogInformation(
                "[Convert] {ConversionType}: file={FileName}, size={SizeKB}KB, elapsed={ElapsedMs}ms",
                conversionType, file.FileName, file.Length / 1024, sw.ElapsedMilliseconds);

            return Content(result, "application/json");
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

    private string GetConversionEndpoint(string source, string target)
    {
        return (source, target) switch
        {
            ("docx", "latex") => "/api/docximport/upload",
            ("docx", "pdf") => "/api/export/pdf",
            ("latex", "pdf") => "/api/latex/compile",
            ("latex", "docx") => "/api/docxexport/from-latex",
            ("markdown", "latex") => "/api/convert/markdown-to-latex",
            _ => throw new ArgumentException($"Unsupported conversion: {source} to {target}")
        };
    }

    private static string[] GetValidExtensions(string format)
    {
        return format switch
        {
            "docx" => new[] { ".docx" },
            "latex" => new[] { ".tex", ".latex" },
            "markdown" => new[] { ".md", ".markdown" },
            "pdf" => new[] { ".pdf" },
            _ => Array.Empty<string>()
        };
    }

    private string GetRateLimitIdentifier()
    {
        // Use user ID if authenticated, otherwise use IP
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
        var resetTime = now.Date.AddDays(1); // Reset at midnight UTC

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

    private class RateLimitEntry
    {
        public int Count { get; set; }
        public DateTime ResetTime { get; set; }
    }
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
