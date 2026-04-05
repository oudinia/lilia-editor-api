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

    public HelpController(IHelpService helpService)
    {
        _helpService = helpService;
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
}
