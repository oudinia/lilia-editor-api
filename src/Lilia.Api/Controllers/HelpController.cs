using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/help")]
[AllowAnonymous]
public class HelpController : ControllerBase
{
    private readonly IHelpService _helpService;
    private readonly IRenderService _renderService;
    private readonly ICompilationQueueService _compilationService;

    public HelpController(
        IHelpService helpService,
        IRenderService renderService,
        ICompilationQueueService compilationService)
    {
        _helpService = helpService;
        _renderService = renderService;
        _compilationService = compilationService;
    }

    [HttpGet]
    public async Task<ActionResult<List<HelpArticleListDto>>> GetAll([FromQuery] string? category = null)
    {
        var articles = await _helpService.GetAllAsync(category);
        return Ok(articles);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<HelpCategoryDto>>> GetCategories()
    {
        var categories = await _helpService.GetCategoriesAsync();
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HelpArticleDetailDto>> GetById(Guid id)
    {
        var article = await _helpService.GetByIdAsync(id);
        if (article == null) return NotFound();
        return Ok(article);
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<HelpArticleDetailDto>> GetBySlug(string slug)
    {
        var article = await _helpService.GetBySlugAsync(slug);
        if (article == null) return NotFound();
        return Ok(article);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<HelpArticleListDto>>> Search([FromQuery] string q = "")
    {
        var results = await _helpService.SearchAsync(q);
        return Ok(results);
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id)
    {
        var article = await _helpService.GetByIdAsync(id);
        if (article == null) return NotFound();

        try
        {
            var latex = await _renderService.RenderToLatexAsync(id);
            var result = await _compilationService.CompileLatexAsync(latex, CompilationType.Pdf);

            if (!result.Success || result.Output == null || result.Output.Length == 0)
                return StatusCode(500, new { error = "PDF compilation failed", details = result.Error });

            var fileName = $"{article.HelpSlug ?? article.Id.ToString()}.pdf";
            return File(result.Output, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to generate PDF", details = ex.Message });
        }
    }
}
