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
    private readonly IValidationCacheService _validationCache;
    private readonly ILogger<LaTeXRenderController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public LaTeXRenderController(
        ILaTeXRenderService latexService,
        IRenderService renderService,
        ICompilationQueueService compilationQueue,
        IAuditService audit,
        IValidationCacheService validationCache,
        ILogger<LaTeXRenderController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _latexService = latexService;
        _renderService = renderService;
        _compilationQueue = compilationQueue;
        _audit = audit;
        _validationCache = validationCache;
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
            // Pick the engine stored on the document so XeLaTeX / LuaLaTeX
            // docs (fontspec / polyglossia / CJK) compile with the right
            // binary. Unknown / empty falls back to pdflatex in ResolveEngine.
            var db = HttpContext.RequestServices.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>();
            var engine = await db.Documents.AsNoTracking()
                .Where(d => d.Id == documentId)
                .Select(d => d.LatexEngine)
                .FirstOrDefaultAsync() ?? "pdflatex";
            var pdf = await _latexService.RenderToPdfAsync(latex, engine);
            return File(pdf, "application/pdf", $"document-{documentId}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render PDF for document {DocumentId}", documentId);
            await _audit.LogAsync("latex_render_error", "document", documentId.ToString(), new
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
            await _audit.LogAsync("latex_render_error", "document", documentId.ToString(), new
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
            await _audit.LogAsync("latex_render_error", "block", blockId.ToString(), new
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
            await _audit.LogAsync("latex_validation_error", "latex", null, new
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

        // Cache check: same content + same rule version → skip the compile.
        // Per-block validation is the hottest validation path (editor calls it
        // on every blur), so cache hits are the vast majority of traffic.
        var contentHash = _validationCache.ComputeHash(block);
        // Look up the authoritative pdflatex result. Typst rows for the
        // same hash live alongside but are surfaced via the rollup + the
        // typst-specific frontend path (two-tier #63).
        var cached = await _validationCache.GetAsync(blockId, contentHash, "pdflatex");
        if (cached is not null)
        {
            return Ok(new
            {
                valid = cached.Status != "error",
                error = cached.ErrorMessage,
                warnings = cached.Warnings is null
                    ? Array.Empty<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<string[]>(cached.Warnings.RootElement.GetRawText()) ?? Array.Empty<string>(),
                blockId,
                cached = true,
                validatedAt = cached.ValidatedAt
            });
        }

        var latex = _renderService.RenderBlockToLatex(block);
        var fullLatex = LaTeXPreamble.WrapForValidation(latex);

        var result = await _latexService.ValidateAsync(fullLatex);
        var (valid, error, warnings) = (result.Valid, result.Error, result.Warnings);

        if (!valid)
        {
            _logger.LogWarning("Block {BlockId} LaTeX validation failed: {Error}", blockId, error);
            await _audit.LogAsync("latex_validation_error", "block", blockId.ToString(), new
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

        // Persist the freshly-computed result + invalidate older hashes for
        // this block so the table doesn't balloon with stale versions.
        var status = !valid ? "error" : (warnings?.Length > 0 ? "warning" : "valid");
        var warningsDoc = warnings is { Length: > 0 }
            ? System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(warnings))
            : null;
        await _validationCache.PersistAsync(new Core.Entities.BlockValidation
        {
            BlockId = blockId,
            DocumentId = block.DocumentId,
            ContentHash = contentHash,
            Status = status,
            ErrorMessage = error,
            Warnings = warningsDoc,
            Validator = "pdflatex",
            RuleVersion = ValidationCacheService.RuleVersion,
            ValidatedAt = DateTime.UtcNow,
        });
        // Await — fire-and-forget on a scoped service races the request scope
        // closing (DbContext gets disposed → transient failure exceptions).
        // Invalidate is a single DELETE, cost is negligible.
        await _validationCache.InvalidateOlderThanAsync(blockId, contentHash, ValidationCacheService.RuleVersion);

        return Ok(new { valid, error, warnings, blockId, cached = false });
    }

    /// <summary>
    /// Document-level validation rollup. Aggregates cached per-block
    /// validation into a single counts DTO via one SQL statement — no
    /// row transits the app. Intended for the editor's document-health
    /// indicator.
    /// </summary>
    [HttpGet("{documentId:guid}/validation-rollup")]
    public async Task<IActionResult> GetValidationRollup(Guid documentId)
    {
        var rollup = await _validationCache.GetDocumentRollupAsync(documentId);
        return Ok(rollup);
    }

    /// <summary>
    /// List per-block validation errors + warnings for a document.
    /// Intended for a "what's broken?" panel and for triaging — caller
    /// gets block_id + status + error_message + validator + first
    /// warning. Scoped to the authenticated user's docs.
    /// </summary>
    [HttpGet("{documentId:guid}/validation-errors")]
    public async Task<IActionResult> ListValidationErrors(Guid documentId, CancellationToken ct)
    {
        var db = HttpContext.RequestServices.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>();
        var rows = await db.BlockValidations
            .AsNoTracking()
            .Where(v => v.DocumentId == documentId && v.Status != "valid")
            .OrderBy(v => v.Validator)
            .ThenByDescending(v => v.ValidatedAt)
            .Select(v => new
            {
                blockId = v.BlockId,
                status = v.Status,
                validator = v.Validator,
                errorMessage = v.ErrorMessage,
                validatedAt = v.ValidatedAt,
                contentHash = v.ContentHash,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// Persist a client-side Typst validation result. The editor WASM
    /// Typst compiler catches syntax/type errors in &lt;150 ms; sending
    /// the verdict here lets the DB-driven cache reflect it alongside
    /// authoritative pdflatex results (two-tier #63).
    /// </summary>
    [HttpPost("block/{blockId:guid}/validate-typst")]
    public async Task<IActionResult> RecordTypstValidation(Guid blockId, [FromBody] RecordTypstValidationRequest req)
    {
        var block = await GetBlockAsync(blockId);
        if (block == null) return NotFound();

        var contentHash = _validationCache.ComputeHash(block);
        var warningsDoc = req.Warnings is { Length: > 0 }
            ? System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(req.Warnings))
            : null;
        var status = !req.Valid ? "error" : (req.Warnings?.Length > 0 ? "warning" : "valid");

        await _validationCache.PersistAsync(new Core.Entities.BlockValidation
        {
            BlockId = blockId,
            DocumentId = block.DocumentId,
            ContentHash = contentHash,
            Status = status,
            ErrorMessage = req.Error,
            Warnings = warningsDoc,
            Validator = "typst",
            RuleVersion = ValidationCacheService.RuleVersion,
            ValidatedAt = DateTime.UtcNow,
        });

        return Ok(new { blockId, contentHash, status, validator = "typst" });
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
            var layoutWarnings = new List<string>();
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

                // Layout hints — fire only when the document is multi-column.
                // We flag figures/tables that don't explicitly span="page" AND
                // long-line code blocks that won't fit in a single column.
                if (doc.Columns >= 2)
                {
                    foreach (var block in doc.Blocks)
                    {
                        var c = block.Content.RootElement;
                        var type = block.Type.ToLowerInvariant();
                        if (type is "figure" or "table" or "image")
                        {
                            var span = c.TryGetProperty("span", out var sp) ? sp.GetString() ?? "column" : "column";
                            if (string.Equals(span, "column", StringComparison.OrdinalIgnoreCase))
                            {
                                layoutWarnings.Add($"[layout] {type} block may not fit a single column — consider setting span to page.");
                            }
                        }
                        else if (type == "code")
                        {
                            var code = c.TryGetProperty("code", out var cd) ? cd.GetString() ?? "" : "";
                            if (code.Split('\n').Any(line => line.Length > 80))
                                layoutWarnings.Add($"[layout] code block has lines >80 chars — will overflow in multi-column layout.");
                        }
                    }
                }
            }

            var latex = await _renderService.RenderToLatexAsync(documentId);
            var result = await _latexService.ValidateAsync(latex);
            var (valid, error, warnings) = (result.Valid, result.Error, result.Warnings);

            // Merge bibliography + layout warnings with LaTeX warnings
            var allWarnings = bibWarnings.Concat(layoutWarnings).Concat(warnings).Distinct().ToArray();

            if (!valid || allWarnings.Length > 0)
            {
                _logger.LogWarning("Document {DocumentId} LaTeX validation: valid={Valid}, errors={Error}, warnings={WarningCount}",
                    documentId, valid, error, allWarnings.Length);
                await _audit.LogAsync("latex_validation_error", "document", documentId.ToString(), new
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

public record RecordTypstValidationRequest(
    bool Valid,
    string? Error,
    string[]? Warnings
);
