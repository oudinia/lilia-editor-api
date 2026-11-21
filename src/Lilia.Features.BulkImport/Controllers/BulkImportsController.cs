using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Features.BulkImport.Controllers;

/// <summary>
/// Bulk import of PDFs (21 Nov 2026, the owner: many scanned maths notebooks at once). The web drops each PDF with its
/// own request (one PDF, field <c>file</c>, up to <c>BulkImport:MaxFileBytes</c>, 95 MB: Cloudflare caps a body at 100 MB)
/// and leaves; each file is split, read and staged in the background (a read-only preview shows it), and its document is
/// made in one go when every part is finished (21 Nov 2026). Only the author sees a
/// batch: anyone else gets 404. A plan refusal is the usual QuotaExceeded answer (402, the global filter).
/// </summary>
[ApiController]
[Route("api/bulk-imports")]
[Authorize]
public sealed class BulkImportsController(BulkImportService service, IServiceProvider services) : ControllerBase
{
    /// <summary>The transport's cap: a little above the 95 MB file, for the multipart wrapping. The file's own cap is the setting's.</summary>
    private const long BodyLimit = 100L * 1024 * 1024;

    private string? UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpPost]
    public async Task<ActionResult<BulkImportCreatedDto>> Create(CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        var id = await service.CreateBatchAsync(userId, ct);
        return StatusCode(StatusCodes.Status201Created, new BulkImportCreatedDto(id));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BulkImportBatchSummaryDto>>> List(CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        return Ok(await service.ListAsync(userId, ct));
    }

    /// <summary>
    /// Staff only (21 Nov 2026): the bulk import timings and precision over the last <paramref name="days"/> days (1–365,
    /// default 7), every author's: per stage, how many measured, p50, p90, min and max (<see cref="BulkImportStatsDto"/>).
    /// Anyone else gets 403. The same numbers from the server: <c>lilia bulk-timings --days N</c>.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<BulkImportStatsDto>> Stats([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        if (services.GetService<Lilia.Api.Services.IPdfImportPipeline>()?.IsStaff(userId) != true) return Forbid();
        return Ok(await service.StatsAsync(days, ct));
    }

    [HttpGet("{batchId:guid}")]
    public async Task<ActionResult<BulkImportBatchDto>> Get(Guid batchId, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        return await service.GetAsync(userId, batchId, ct) is { } batch ? Ok(batch) : NotFound();
    }

    [HttpPost("{batchId:guid}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(BodyLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = BodyLimit)]
    public async Task<ActionResult<BulkImportFileDto>> AddFile(Guid batchId, IFormFile? file, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        if (file is null || file.Length == 0) return Problem400("Send one PDF in the field \"file\".");
        // Without Mathpix there is nothing to read the pages with: refused before anything is kept.
        if (services.GetService<IMathpixClient>() is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "PDF import is not available on this server." });

        AddFileResult result;
        try
        {
            await using var stream = file.OpenReadStream();
            result = await service.AddFileAsync(userId, batchId, stream, file.FileName, ct);
        }
        catch (InvalidOperationException ex)
        {
            // Plans can't be checked: refused, as the single PDF import (fails closed).
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        return result.Refusal switch
        {
            AddFileRefusal.None => Accepted(result.File),
            AddFileRefusal.BatchNotFound => NotFound(),
            AddFileRefusal.TooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = result.Message }),
            AddFileRefusal.BatchClosed or AddFileRefusal.TooManyFiles => Conflict(new { error = result.Message }),
            _ => Problem400(result.Message ?? "This file can't be imported."),
        };
    }

    [HttpPost("{batchId:guid}/files/{fileId:guid}/retry")]
    public async Task<ActionResult<BulkImportFileDto>> Retry(Guid batchId, Guid fileId, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        try
        {
            var (retried, dto) = await service.RetryFileAsync(userId, batchId, fileId, ct);
            if (retried is null) return NotFound();
            return retried.Value ? Accepted(dto) : Conflict(new { error = "Nothing to retry in this file.", file = dto });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// The file's read-only preview while it imports (21 Nov 2026): its staged blocks in page order and a placeholder per
    /// chunk not staged yet (<see cref="BulkImportPreviewDto"/>). Only the batch's author: 404 for anyone else.
    /// </summary>
    [HttpGet("{batchId:guid}/files/{fileId:guid}/preview")]
    public async Task<ActionResult<BulkImportPreviewDto>> Preview(Guid batchId, Guid fileId, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        return await service.PreviewAsync(userId, batchId, fileId, ct) is { } preview ? Ok(preview) : NotFound();
    }

    /// <summary>
    /// A staged figure's image for the preview (21 Nov 2026): raster types only (PNG, JPEG, GIF, WebP), served with
    /// <c>nosniff</c>. Only the batch's author: 404 for anyone else, and for a figure no longer staged (promoted: the
    /// document's asset has it).
    /// </summary>
    [HttpGet("{batchId:guid}/files/{fileId:guid}/assets/{name}")]
    public async Task<IActionResult> StagedAsset(Guid batchId, Guid fileId, string name, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        if (await service.StagedAssetAsync(userId, batchId, fileId, name, ct) is not { } asset) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.ContentDisposition = "inline";
        return File(asset.Content, asset.ContentType);
    }

    /// <summary>
    /// Stops one file (21 Nov 2026): before its document is made, its staging is deleted and no document is made; nothing
    /// is charged for pages not read. 409 when it has nothing left to stop.
    /// </summary>
    [HttpPost("{batchId:guid}/files/{fileId:guid}/cancel")]
    public async Task<ActionResult<BulkImportFileDto>> CancelFile(Guid batchId, Guid fileId, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        var (cancelled, dto) = await service.CancelFileAsync(userId, batchId, fileId, ct);
        if (cancelled is null) return NotFound();
        return cancelled.Value ? Accepted(dto) : Conflict(new { error = "This file has nothing left to stop.", file = dto });
    }

    [HttpPost("{batchId:guid}/cancel")]
    public async Task<ActionResult<BulkImportBatchDto>> Cancel(Guid batchId, CancellationToken ct)
    {
        if (UserId is not { Length: > 0 } userId) return Unauthorized();
        return await service.CancelAsync(userId, batchId, ct) is { } batch ? Accepted(batch) : NotFound();
    }

    private BadRequestObjectResult Problem400(string message) => BadRequest(new { error = message });
}
