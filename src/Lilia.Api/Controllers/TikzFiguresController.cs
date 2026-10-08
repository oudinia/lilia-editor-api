using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Lilia.Api.Filters;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines.Themes;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// A TikZ figure's drawing (TikZ figures, step 1; drafts and theme colours, step 3).
///
/// <para><c>GET …/figure.svg</c>: the SVG, drawn on demand and cached by content
/// (<see cref="TikzFigureService"/>). When it does not draw: 422 with
/// <c>{ kind: "tex"|"timeout"|"budget", message, line, excerpt, column, hasLastGood }</c>,
/// <c>line</c> 1-based in the figure's own source. <c>?lastGood=true</c>: the last SVG this
/// block drew, or 404.</para>
///
/// <para><c>POST …/figure/draft</c> <c>{ source }</c> (write access): the split view's drawing of
/// unsaved source, with the same answers as figure.svg, through the same cache, without touching
/// the block's last good drawing. 409 <c>{ kind: "superseded" }</c> when a newer draft of the
/// same figure replaced it; an aborted request kills its compile.</para>
///
/// <para><c>GET …/figure/colours</c>: the theme colour names resolved for this figure
/// (<c>{ "lilia-ink": "#2F2E2C", …, "lilia-chapter": "#B8303A" }</c>), for the editor's swatches
/// and autocomplete.</para>
///
/// <para>Drawings carry <c>X-Draw-Ms</c> (the server's time to draw, cache included) and
/// <c>X-Lilia-Cache: hit|miss</c>.</para>
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
        var (block, notFound) = await FigureAsync(documentId, blockId, ct);
        if (block is null) return notFound!;

        if (lastGood)
        {
            var previous = _tikz.LastGood(blockId);
            return previous is null ? NotFound(new { message = "This figure has not drawn yet." }) : Svg(previous);
        }

        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return NotFound();
        var themes = await TikzFigureThemes.LoadAsync(_db, doc, ct);

        var clock = Stopwatch.StartNew();
        var result = await _tikz.RenderAsync(doc, block, CallerId(), ct, themes.For(blockId));
        return Drawing(result, blockId, clock);
    }

    /// <summary>The split view's drawing of unsaved source (step 3, 1a).</summary>
    [HttpPost("figure/draft")]
    [RequireDocumentAccess(Permissions.Write)]
    public async Task<IActionResult> Draft(Guid documentId, Guid blockId, [FromBody] TikzDraftRequest? body)
    {
        // The request's own token: the editor aborts the older request when a newer keystroke draws.
        var ct = HttpContext.RequestAborted;
        if (body?.Source is not { } source)
            return BadRequest(new { message = "A draft needs its source." });
        if (source.Length > TikzFigureService.MaxDraftChars)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "This source is too long to draw." });

        // A table draws too: Plot this table (step 4a) previews the plot that will follow it,
        // before the figure exists, with the colours of the place it will go.
        var (block, notFound) = await FigureAsync(documentId, blockId, ct, allowTable: true);
        if (block is null) return notFound!;
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return NotFound();
        var themes = await TikzFigureThemes.LoadAsync(_db, doc, ct);

        var clock = Stopwatch.StartNew();
        try
        {
            var result = await _tikz.DraftAsync(doc, block, source, CallerId(), themes.For(blockId), ct);
            return Drawing(result, blockId, clock);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Conflict(new { kind = "superseded", message = "A newer draft of this figure replaced this one." });
        }
        catch (OperationCanceledException)
        {
            // The editor gave up on this request: nobody reads the answer.
            return StatusCode(499);
        }
    }

    /// <summary>The theme colour names resolved for this figure (Index: its chapter's colour).</summary>
    [HttpGet("figure/colours")]
    public async Task<IActionResult> GetColours(Guid documentId, Guid blockId, CancellationToken ct = default)
    {
        // A table too: the plot dialog's series swatches, for the place the plot will go (step 4a).
        var (block, notFound) = await FigureAsync(documentId, blockId, ct, allowTable: true);
        if (block is null) return notFound!;
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return NotFound();
        var themes = await TikzFigureThemes.LoadAsync(_db, doc, ct);
        return Ok(FigureColours.Resolve(themes.For(blockId)));
    }

    /// <summary>The block when it is a TikZ figure of this document, else the 404 to answer.</summary>
    private async Task<(Block? Block, IActionResult? NotFound)> FigureAsync(Guid documentId, Guid blockId, CancellationToken ct, bool allowTable = false)
    {
        var block = await _db.Blocks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == blockId && b.DocumentId == documentId, ct);
        if (block is null) return (null, NotFound());
        if (allowTable && block.Type == "table") return (block, null);
        if (block.Type is not ("figure" or "image") || !TikzFigure.IsTikz(block.Content.RootElement))
            return (null, NotFound(new { message = "This block is not a TikZ figure." }));
        return (block, null);
    }

    private string CallerId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? "anonymous";

    /// <summary>The SVG, or 422 with where it went wrong; with the time it took and whether it came from the cache.</summary>
    private IActionResult Drawing(TikzRenderResult result, Guid blockId, Stopwatch clock)
    {
        Response.Headers["X-Lilia-Cache"] = result.Cached ? "hit" : "miss";
        Response.Headers["X-Draw-Ms"] = clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
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

/// <summary><c>POST …/figure/draft</c>: the unsaved TikZ source to draw.</summary>
public sealed record TikzDraftRequest(string? Source);
