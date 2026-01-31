using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PreferencesController : ControllerBase
{
    private readonly IPreferencesService _preferencesService;

    public PreferencesController(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<UserPreferencesDto>> GetPreferences()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var preferences = await _preferencesService.GetPreferencesAsync(userId);
        return Ok(preferences);
    }

    [HttpPut]
    public async Task<ActionResult<UserPreferencesDto>> UpdatePreferences([FromBody] UpdatePreferencesDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var preferences = await _preferencesService.UpdatePreferencesAsync(userId, dto);
        return Ok(preferences);
    }

    [HttpGet("shortcuts")]
    public async Task<ActionResult<UserPreferencesDto>> GetShortcuts()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var preferences = await _preferencesService.GetPreferencesAsync(userId);
        return Ok(preferences);
    }

    [HttpPut("shortcuts")]
    public async Task<ActionResult<UserPreferencesDto>> UpdateShortcuts([FromBody] UpdateKeyboardShortcutsDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var preferences = await _preferencesService.UpdateKeyboardShortcutsAsync(userId, dto);
        return Ok(preferences);
    }
}
