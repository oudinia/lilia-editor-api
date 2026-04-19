using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IEntitlementService _entitlement;

    public MeController(IEntitlementService entitlement)
    {
        _entitlement = entitlement;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Return the authenticated user's active plan + caps + features.
    /// Frontend calls this once on app boot to drive the useActivePlan
    /// hook (pricing page reads, sidebar filtering, quota pre-flight
    /// UI). Response is safe to cache for ~60s per session.
    /// </summary>
    [HttpGet("plan")]
    public async Task<IActionResult> GetActivePlan(CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var plan = await _entitlement.GetActivePlanAsync(userId, ct);
        if (plan is null) return Ok(new { slug = (string?)null, caps = new { }, features = Array.Empty<string>() });

        return Ok(plan);
    }

    /// <summary>AI credit balance (always current).</summary>
    [HttpGet("credits")]
    public async Task<IActionResult> GetAiCreditBalance(CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var balance = await _entitlement.GetAiCreditBalanceAsync(userId, ct);
        return Ok(new { balance });
    }
}
