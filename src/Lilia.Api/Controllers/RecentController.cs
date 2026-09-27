using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// What the signed-in user did recently, per tool — for Home and the
/// switcher's Recent list.
/// </summary>
[ApiController]
[Route("api/recent")]
[Authorize]
public class RecentController : ControllerBase
{
    private readonly IRecentActivityService _recent;

    public RecentController(IRecentActivityService recent) => _recent = recent;

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Up to <paramref name="perTool"/> recent items (1–10, default 3) for each
    /// tool the user has used, tools most recent first. Unused tools are omitted.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RecentActivityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecentActivityDto>> Get([FromQuery] int perTool = RecentActivityService.DefaultPerTool)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _recent.GetRecentAsync(userId, perTool));
    }
}
