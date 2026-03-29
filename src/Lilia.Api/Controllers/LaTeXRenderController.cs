using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/latex")]
[Authorize]
public class LaTeXRenderController : ControllerBase
{
    private readonly ILaTeXRenderService _latexService;
    private readonly IRenderService _renderService;
    private readonly ILogger<LaTeXRenderController> _logger;

    public LaTeXRenderController(
        ILaTeXRenderService latexService,
        IRenderService renderService,
        ILogger<LaTeXRenderController> logger)
    {
        _latexService = latexService;
        _renderService = renderService;
        _logger = logger;
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
    /// Validate LaTeX source without rendering.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateLatex([FromBody] ValidateLatexRequest request)
    {
        var (valid, error, warnings) = await _latexService.ValidateAsync(request.Latex);
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
        // Wrap in a minimal document for validation
        var fullLatex = $@"\documentclass{{article}}
\usepackage{{amsmath,amssymb,amsfonts}}
\usepackage{{mathtools}}
\usepackage{{graphicx}}
\usepackage{{booktabs}}
\usepackage{{listings}}
\usepackage{{hyperref}}
\begin{{document}}
{latex}
\end{{document}}";

        var (valid, error, warnings) = await _latexService.ValidateAsync(fullLatex);
        return Ok(new { valid, error, warnings, blockId });
    }

    /// <summary>
    /// Validate an entire document's LaTeX.
    /// </summary>
    [HttpPost("{documentId:guid}/validate")]
    public async Task<IActionResult> ValidateDocument(Guid documentId)
    {
        try
        {
            var latex = await _renderService.RenderToLatexAsync(documentId);
            var (valid, error, warnings) = await _latexService.ValidateAsync(latex);
            return Ok(new { valid, error, warnings });
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
}

public record RenderLatexRequest(
    string Latex,
    string Format = "pdf",
    int Dpi = 150,
    int Timeout = 30
);

public record ValidateLatexRequest(string Latex);
