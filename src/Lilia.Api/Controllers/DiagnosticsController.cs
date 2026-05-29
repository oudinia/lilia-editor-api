using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lilia.Api.Controllers;

/// <summary>
/// Client-triggered diagnostic capture store. Lets the math editor's
/// DevTools panel (or any other surface) persist a state + log
/// bundle under a short ref token so the user can share it for
/// analysis.
///
/// Access policy is intentionally simple: owners can read their own
/// captures, nobody else can. Admins are deferred — when an
/// IUserService.IsAdminAsync lands we can flip the GET branch.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiagnosticsController : ControllerBase
{
    private readonly IDiagnosticCaptureService _captures;

    public DiagnosticsController(IDiagnosticCaptureService captures)
    {
        _captures = captures;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Persist a new capture bundle. Returns the short ref token
    /// the user can paste when reporting.
    /// </summary>
    [HttpPost("captures")]
    public async Task<ActionResult<DiagnosticCaptureCreatedDto>> Create([FromBody] CreateDiagnosticCaptureDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var created = await _captures.CreateAsync(userId, dto);
        return CreatedAtAction(nameof(GetByRefToken), new { refToken = created.RefToken }, created);
    }

    /// <summary>
    /// Look up a capture by its short ref token. Returns 404 for
    /// unknown tokens or tokens the requester does not own.
    /// </summary>
    [HttpGet("captures/{refToken}")]
    public async Task<ActionResult<DiagnosticCaptureDto>> GetByRefToken(string refToken)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        // Admin-flag is hard-coded false until the role plumbing
        // lands — owner-only reads keep the surface privacy-safe.
        var capture = await _captures.GetByRefTokenAsync(refToken, userId, isAdmin: false);
        if (capture is null) return NotFound();
        return Ok(capture);
    }

    /// <summary>
    /// List the requester's own captures (newest first). Surfaces
    /// the "share this earlier capture" UX in the DevTools panel.
    /// </summary>
    [HttpGet("captures")]
    public async Task<ActionResult<List<DiagnosticCaptureDto>>> ListMine([FromQuery] int limit = 20)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var items = await _captures.ListMineAsync(userId, limit);
        return Ok(items);
    }
}
