using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Core.Models.Epub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/epub")]
[Authorize]
public class EpubController : ControllerBase
{
    private readonly IEpubService _epubService;
    private readonly ILogger<EpubController> _logger;

    public EpubController(IEpubService epubService, ILogger<EpubController> logger)
    {
        _epubService = epubService;
        _logger = logger;
    }

    /// <summary>
    /// Analyze an ePub file and return a report of issues and statistics.
    /// </summary>
    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "File must be an .epub file" });

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        _logger.LogInformation("[ePub] Analyzing file {FileName} ({Size} bytes) for user {UserId}",
            file.FileName, file.Length, userId);

        using var stream = file.OpenReadStream();
        var result = await _epubService.AnalyzeAsync(stream);
        return Ok(result);
    }

    /// <summary>
    /// Import an ePub file and return extracted blocks and metadata.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "File must be an .epub file" });

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        _logger.LogInformation("[ePub] Importing file {FileName} for user {UserId}", file.FileName, userId);

        using var stream = file.OpenReadStream();
        var (metadata, blocks, warnings) = await _epubService.ImportAsync(stream);

        return Ok(new
        {
            metadata,
            blocks = blocks.Select(b => new
            {
                b.Id,
                b.Type,
                content = JsonSerializer.Deserialize<JsonElement>(b.Content.RootElement.GetRawText()),
                b.SortOrder
            }),
            warnings
        });
    }

    /// <summary>
    /// Export blocks as an ePub file.
    /// </summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ExportRequest request)
    {
        if (request.Blocks == null || request.Blocks.Count == 0)
            return BadRequest(new { error = "No blocks provided" });

        if (request.Options == null)
            return BadRequest(new { error = "Export options are required" });

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        _logger.LogInformation("[ePub] Exporting {BlockCount} blocks as ePub for user {UserId}",
            request.Blocks.Count, userId);

        var blocks = request.Blocks.Select((b, i) => new Block
        {
            Id = b.Id ?? Guid.NewGuid(),
            Type = b.Type,
            Content = JsonDocument.Parse(b.Content.GetRawText()),
            SortOrder = b.SortOrder ?? i
        }).ToList();

        var epubBytes = await _epubService.ExportAsync(blocks, request.Options);

        var sanitizedTitle = SanitizeFilename(request.Options.Title);
        return File(epubBytes, "application/epub+zip", $"{sanitizedTitle}.epub");
    }

    /// <summary>
    /// Clean and repackage an ePub file with normalized formatting.
    /// </summary>
    [HttpPost("clean")]
    public async Task<IActionResult> Clean(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "File must be an .epub file" });

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "No valid authentication token provided" });

        _logger.LogInformation("[ePub] Cleaning file {FileName} for user {UserId}", file.FileName, userId);

        using var stream = file.OpenReadStream();
        var cleanedBytes = await _epubService.CleanAndRepackageAsync(stream);

        var sanitizedName = SanitizeFilename(Path.GetFileNameWithoutExtension(file.FileName));
        return File(cleanedBytes, "application/epub+zip", $"{sanitizedName}_clean.epub");
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

public class ExportRequest
{
    public List<ExportBlockDto> Blocks { get; set; } = new();
    public EpubExportOptions? Options { get; set; }
}

public class ExportBlockDto
{
    public Guid? Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public JsonElement Content { get; set; }
    public int? SortOrder { get; set; }
}
