using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lilia.Api.Controllers;

public sealed record PastePreviewRequest(string? Content, string? Format);

/// <summary>
/// Paste → LaTeX, phase 2 (11 Nov 2026): the preview Import → Paste shows before anything is made. It compiles, so
/// it is held to the strict rate (10 a minute) and a size a paste can reach.
/// </summary>
[ApiController]
[Route("api/lilia/paste")]
[Authorize]
[EnableRateLimiting("strict")]
public class PasteController(IPastePreviewService previews) : ControllerBase
{
    /// <summary>Half a megabyte of text: a long thesis chapter, far more than a paste.</summary>
    public const int MaxChars = 512 * 1024;

    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase) { "AUTO", "LATEX", "LML", "MARKDOWN" };

    [HttpPost("preview")]
    public async Task<ActionResult<PastePreview>> Preview([FromBody] PastePreviewRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Paste some text first." });
        if (request.Content.Length > MaxChars)
            return BadRequest(new { message = "This is longer than a paste can be checked (half a megabyte). Import it as a file instead." });
        var format = string.IsNullOrWhiteSpace(request.Format) ? "AUTO" : request.Format!;
        if (!Formats.Contains(format))
            return BadRequest(new { message = $"Unknown format '{format}': use AUTO, LATEX, LML or MARKDOWN." });
        return Ok(await previews.PreviewAsync(request.Content, format, ct));
    }
}
