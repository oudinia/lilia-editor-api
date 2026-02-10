using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FormulasController : ControllerBase
{
    private readonly IFormulaService _formulaService;

    public FormulasController(IFormulaService formulaService)
    {
        _formulaService = formulaService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<FormulaPageDto>> GetFormulas([FromQuery] FormulaSearchDto search)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _formulaService.GetFormulasAsync(userId, search);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FormulaDto>> GetFormula(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var formula = await _formulaService.GetFormulaAsync(id, userId);
        if (formula == null) return NotFound();
        return Ok(formula);
    }

    [HttpPost]
    public async Task<ActionResult<FormulaDto>> CreateFormula([FromBody] CreateFormulaDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var formula = await _formulaService.CreateFormulaAsync(userId, dto);
        return CreatedAtAction(nameof(GetFormula), new { id = formula.Id }, formula);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FormulaDto>> UpdateFormula(Guid id, [FromBody] UpdateFormulaDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var formula = await _formulaService.UpdateFormulaAsync(id, userId, dto);
        if (formula == null) return NotFound();
        return Ok(formula);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteFormula(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _formulaService.DeleteFormulaAsync(id, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/favorite")]
    public async Task<ActionResult<FormulaDto>> ToggleFavorite(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var formula = await _formulaService.ToggleFavoriteAsync(id, userId);
        if (formula == null) return NotFound();
        return Ok(formula);
    }

    [HttpPost("{id:guid}/use")]
    public async Task<ActionResult<object>> UseFormula(Guid id, [FromQuery] string? label = null)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var lml = await _formulaService.IncrementUsageAsync(id, userId, label);
        if (lml == null) return NotFound();
        return Ok(new { lml });
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories()
    {
        var categories = await _formulaService.GetCategoriesAsync();
        return Ok(categories);
    }
}
