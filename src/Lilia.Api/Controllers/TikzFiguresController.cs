using System.Security.Cryptography;
using Lilia.Api.Filters;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// A TikZ figure's drawing (TikZ figures, step 1).
///
/// <para><c>GET …/figure.svg</c>: the SVG, drawn on demand and cached by content
/// (<see cref="TikzFigureService"/>). When it does not draw: 422 with
/// <c>{ kind: "tex"|"timeout"|"budget", message, line, excerpt, column, hasLastGood }</c>,
/// <c>line</c> 1-based in the figure's own source. <c>?lastGood=true</c>: the last SVG this
/// block drew, or 404.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/documents/{documentId:guid}/blocks/{blockId:guid}")]
[RequireDocumentAccess(Permissions.Read)]
[EnableRateLimiting("per-user")]
public class TikzFiguresController : ControllerBase
{
    private readonly LiliaDbContext _db;
    private readonly ITikzFigureService _tikz;

    public TikzFiguresController(LiliaDbContext db, ITikzFigureService tikz)
    {
        _db = db;
        _tikz = tikz;
    }

    [HttpGet("figure.svg")]
    public async Task<IActionResult> GetSvg(Guid documentId, Guid blockId, [FromQuery] bool lastGood = false, CancellationToken ct = default)
    {
        var block = await _db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == blockId && b.DocumentId == documentId, ct);
        if (block is null) return NotFound();
        if (block.Type is not ("figure" or "image") || !TikzFigure.IsTikz(block.Content.RootElement))
            return NotFound(new { message = "This block is not a TikZ figure." });

        if (lastGood)
        {
            var previous = _tikz.LastGood(blockId);
            return previous is null ? NotFound(new { message = "This figure has not drawn yet." }) : Svg(previous);
        }

        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return NotFound();

        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? "anonymous";
        var result = await _tikz.RenderAsync(doc, block, userId, ct);
        Response.Headers["X-Lilia-Cache"] = result.Cached ? "hit" : "miss";
        if (result.Svg is { } svg) return Svg(svg);

        var e = result.Error!;
        return UnprocessableEntity(new
        {
            kind = e.Kind,
            message = e.Message,
            line = e.Line,
            excerpt = e.Excerpt,
            column = e.Column is { } c ? new { start = c.Start, end = c.End } : null,
            hasLastGood = _tikz.HasLastGood(blockId),
        });
    }

    /// <summary>The SVG with an ETag of its bytes, so a canvas showing it again gets a 304.</summary>
    private IActionResult Svg(byte[] svg)
    {
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(svg))[..32].ToLowerInvariant() + "\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, no-cache";
        if (Request.Headers.IfNoneMatch.Any(v => v == etag)) return StatusCode(StatusCodes.Status304NotModified);
        return File(svg, "image/svg+xml");
    }
}
