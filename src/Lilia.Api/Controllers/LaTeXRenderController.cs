using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/latex")]
[Authorize]
public class LaTeXRenderController : ControllerBase
{
    private readonly ILaTeXRenderService _latexService;
    private readonly IRenderService _renderService;
    private readonly ICompilationQueueService _compilationQueue;
    private readonly IAuditService _audit;
    private readonly ILogger<LaTeXRenderController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public LaTeXRenderController(
        ILaTeXRenderService latexService,
        IRenderService renderService,
        ICompilationQueueService compilationQueue,
        IAuditService audit,
        ILogger<LaTeXRenderController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _latexService = latexService;
        _renderService = renderService;
        _compilationQueue = compilationQueue;
        _audit = audit;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Get compilation queue metrics.
    /// </summary>
    [HttpGet("metrics")]
    [AllowAnonymous]
    public IActionResult GetMetrics()
    {
        return Ok(new
        {
            queueLength = _compilationQueue.QueueLength,
            activeCompilations = _compilationQueue.ActiveCompilations,
            cacheHitRate = Math.Round(_compilationQueue.CacheHitRate, 2),
            avgCompilationTimeMs = Math.Round(_compilationQueue.AvgCompilationTimeMs, 2)
        });
    }

    /// <summary>
    /// Render a full document's LaTeX to PDF.
    /// </summary>
    [HttpPost("{documentId:guid}/pdf")]
    public async Task<IActionResult> RenderDocumentPdf(Guid documentId)
    {
        try
        {
            var latex = await _renderService.RenderToLatexAsync(documentId);
            var pdf = await _latexService.RenderToPdfAsync(latex);
            return File(pdf, "application/pdf", $"document-{documentId}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render PDF for document {DocumentId}", documentId);
            _ = _audit.LogAsync("latex_render_error", "document", documentId.ToString(), new
            {
                format = "pdf",
                error = ex.Message
            });
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Render a full document's LaTeX to PNG preview.
    /// </summary>
    [HttpPost("{documentId:guid}/png")]
    public async Task<IActionResult> RenderDocumentPng(Guid documentId, [FromQuery] int dpi = 150)
    {
        try
        {
            var latex = await _renderService.RenderToLatexAsync(documentId);
            var png = await _latexService.RenderToPngAsync(latex, dpi);
            return File(png, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render PNG for document {DocumentId}", documentId);
            _ = _audit.LogAsync("latex_render_error", "document", documentId.ToString(), new
            {
                format = "png",
                error = ex.Message
            });
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Render a single block's LaTeX to PNG.
    /// </summary>
    [HttpPost("block/{blockId:guid}/png")]
    public async Task<IActionResult> RenderBlockPng(Guid blockId, [FromQuery] int dpi = 150)
    {
        try
        {
            // Get block and render to LaTeX
            var block = await GetBlockAsync(blockId);
            if (block == null) return NotFound();

            var latex = _renderService.RenderBlockToLatex(block);
            var png = await _latexService.RenderBlockToPngAsync(latex, dpi: dpi);
            return File(png, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render block {BlockId}", blockId);
            _ = _audit.LogAsync("latex_render_error", "block", blockId.ToString(), new
            {
                format = "png",
                error = ex.Message
            });
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Render arbitrary LaTeX source to PDF.
    /// </summary>
    [HttpPost("render")]
    public async Task<IActionResult> RenderRawLatex([FromBody] RenderLatexRequest request)
    {
        try
        {
            var result = request.Format switch
            {
                "pdf" => await _latexService.RenderToPdfAsync(request.Latex, request.Timeout),
                "png" => await _latexService.RenderToPngAsync(request.Latex, request.Dpi, request.Timeout),
                _ => await _latexService.RenderToPdfAsync(request.Latex, request.Timeout),
            };

            var contentType = request.Format == "png" ? "image/png" : "application/pdf";
            return File(result, contentType);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Render a LaTeX formula to SVG. Supports caching via ETag.
    /// </summary>
    [HttpGet("svg")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public async Task<IActionResult> RenderSvg([FromQuery] string latex, [FromQuery] bool display = true)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return BadRequest(new { error = "latex parameter is required" });

        // ETag for client-side caching
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{latex}:{display}")
        ))[..16];

        if (Request.Headers.IfNoneMatch.ToString() == $"\"{hash}\"")
            return StatusCode(304);

        try
        {
            var svg = await _latexService.RenderToSvgAsync(latex, display);
            Response.Headers.ETag = $"\"{hash}\"";
            Response.Headers.CacheControl = "public, max-age=86400, immutable";
            return Content(svg, "image/svg+xml");
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Validate LaTeX source without rendering.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateLatex([FromBody] ValidateLatexRequest request)
    {
        var result = await _latexService.ValidateAsync(request.Latex);
        var (valid, error, warnings) = (result.Valid, result.Error, result.Warnings);

        if (!valid)
        {
            _logger.LogWarning("LaTeX validation failed: {Error}", error);
            _ = _audit.LogAsync("latex_validation_error", "latex", null, new
            {
                error,
                warnings,
                errorCategory = result.ParsedError?.Category,
                errorToken = result.ParsedError?.Token,
                latexPreview = request.Latex.Length > 500 ? request.Latex[..500] : request.Latex
            });
        }

        PersistCompilationEvent(result, "validate");
        return Ok(new { valid, error, warnings });
    }

    /// <summary>
    /// Validate a single block's LaTeX.
    /// </summary>
    [HttpPost("block/{blockId:guid}/validate")]
    public async Task<IActionResult> ValidateBlock(Guid blockId)
    {
        var block = await GetBlockAsync(blockId);
        if (block == null) return NotFound();

        var latex = _renderService.RenderBlockToLatex(block);
        var fullLatex = LaTeXPreamble.WrapForValidation(latex);

        var result = await _latexService.ValidateAsync(fullLatex);
        var (valid, error, warnings) = (result.Valid, result.Error, result.Warnings);

        if (!valid)
        {
            _logger.LogWarning("Block {BlockId} LaTeX validation failed: {Error}", blockId, error);
            _ = _audit.LogAsync("latex_validation_error", "block", blockId.ToString(), new
            {
                error,
                warnings,
                errorCategory = result.ParsedError?.Category,
                errorToken = result.ParsedError?.Token,
                blockType = block.Type,
                latexPreview = latex.Length > 500 ? latex[..500] : latex
            });
        }

        PersistCompilationEvent(result, "validate_block",
            documentId: block.DocumentId, blockId: blockId, blockType: block.Type);
        return Ok(new { valid, error, warnings, blockId });
    }

    /// <summary>
    /// Validate an entire document's LaTeX + check bibliography references.
    /// </summary>
    [HttpPost("{documentId:guid}/validate")]
    public async Task<IActionResult> ValidateDocument(Guid documentId)
    {
        try
        {
            var db = HttpContext.RequestServices.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>();

            // Check for missing bibliography cite keys
            var doc = await db.Documents
                .Include(d => d.Blocks)
                .Include(d => d.BibliographyEntries)
                .FirstOrDefaultAsync(d => d.Id == documentId);

            var bibWarnings = new List<string>();
            if (doc != null)
            {
                var bibKeys = doc.BibliographyEntries.Select(e => e.CiteKey).ToHashSet();
                var citeRegex = new System.Text.RegularExpressions.Regex(@"\\cite\{([^}]+)\}");

                foreach (var block in doc.Blocks)
                {
                    var content = block.Content.RootElement;
                    var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    foreach (System.Text.RegularExpressions.Match match in citeRegex.Matches(text))
                    {
                        var keys = match.Groups[1].Value.Split(',').Select(k => k.Trim());
                        foreach (var key in keys)
                        {
                            if (!bibKeys.Contains(key))
                                bibWarnings.Add($"Missing bibliography entry: \\cite{{{key}}}");
                        }
                    }
                }
            }

            var latex = await _renderService.RenderToLatexAsync(documentId);
            var result = await _latexService.ValidateAsync(latex);
            var (valid, error, warnings) = (result.Valid, result.Error, result.Warnings);

            // Merge bibliography warnings with LaTeX warnings
            var allWarnings = bibWarnings.Concat(warnings).Distinct().ToArray();

            if (!valid || allWarnings.Length > 0)
            {
                _logger.LogWarning("Document {DocumentId} LaTeX validation: valid={Valid}, errors={Error}, warnings={WarningCount}",
                    documentId, valid, error, allWarnings.Length);
                _ = _audit.LogAsync("latex_validation_error", "document", documentId.ToString(), new
                {
                    valid,
                    error,
                    errorCategory = result.ParsedError?.Category,
                    errorToken = result.ParsedError?.Token,
                    warnings = allWarnings,
                    blockCount = doc?.Blocks.Count,
                    bibWarnings = bibWarnings.Count > 0 ? bibWarnings : null
                });
            }

            PersistCompilationEvent(result, "validate_document", documentId: documentId);

            // Persist validation summary to document so it appears in the list view badge
            if (doc != null)
            {
                doc.ValidationErrorCount = valid ? 0 : 1;
                doc.ValidationWarningCount = allWarnings.Length;
                doc.ValidationCheckedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Ok(new { valid, error, warnings = allWarnings });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private async Task<Lilia.Core.Entities.Block?> GetBlockAsync(Guid blockId)
    {
        var db = HttpContext.RequestServices.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>();
        return await db.Blocks.FindAsync(blockId);
    }

    /// <summary>
    /// Persists a LaTeXCompilationEvent row for telemetry / error frequency analysis.
    /// Fire-and-forget — never throws.
    /// </summary>
    private void PersistCompilationEvent(
        LatexValidationResult result,
        string eventType,
        Guid? documentId = null,
        Guid? blockId = null,
        string? blockType = null)
    {
        // Capture values from request context before Task.Run — HttpContext
        // is NOT safe to access from a background thread after the request ends.
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;

        _ = Task.Run(async () =>
        {
            try
            {
                // Open a dedicated scope so we get a fresh DbContext that is
                // not shared with the request scope. Using the request-scoped
                // context here caused "A second operation was started on this
                // context instance before a previous operation completed" because
                // the fire-and-forget SaveChangesAsync raced with the main
                // request's SaveChangesAsync in ValidateDocument.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>();

                var evt = new LaTeXCompilationEvent
                {
                    DocumentId = documentId,
                    BlockId = blockId,
                    BlockType = blockType,
                    EventType = eventType,
                    Success = result.Valid,
                    ErrorRaw = result.ParsedError?.ErrorRaw,
                    ErrorCategory = result.ParsedError?.Category,
                    ErrorToken = result.ParsedError?.Token,
                    ErrorLine = result.ParsedError?.LineNumber,
                    WarningCount = result.Warnings.Length,
                    DurationMs = result.DurationMs,
                    UserId = userId,
                };

                db.LaTeXCompilationEvents.Add(evt);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist LaTeXCompilationEvent (non-critical)");
            }
        });
    }
}

public record RenderLatexRequest(
    string Latex,
    string Format = "pdf",
    int Dpi = 150,
    int Timeout = 30
);

public record ValidateLatexRequest(string Latex);
