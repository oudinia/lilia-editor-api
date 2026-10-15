using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

    public record ProfileDto(string Id, string Email, string? Name, string? Image);
    public record UpdateProfileRequest(string? Name);

    /// <summary>
    /// The profile as collaborators see it: the name on comments, shares and presence (Settings →
    /// Profile, launch redesign 3a). The email comes from sign-in and changes there, not here.
    /// </summary>
    [HttpGet("profile")]
    public async Task<ActionResult<ProfileDto>> GetProfile([FromServices] LiliaDbContext db, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? NotFound() : new ProfileDto(user.Id, user.Email, user.Name, user.Image);
    }

    /// <summary>Change the name others see. 1–120 characters, trimmed.</summary>
    [HttpPut("profile")]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest req, [FromServices] LiliaDbContext db, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
            return BadRequest(new { code = "VALIDATION_FAILED", message = "A name is 1 to 120 characters." });
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();
        user.Name = name;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new ProfileDto(user.Id, user.Email, user.Name, user.Image);
    }

    /// <summary>Start "export everything" (Settings → Your data); returns the running one if there is one.</summary>
    [HttpPost("export")]
    public async Task<ActionResult<AccountExportDto>> StartExport([FromServices] AccountExportService exports, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Accepted(await exports.StartAsync(userId, ct));
    }

    /// <summary>The latest export: pending, processing, completed (until it expires) or failed. 204 when none.</summary>
    [HttpGet("export")]
    public async Task<ActionResult<AccountExportDto>> LatestExport([FromServices] AccountExportService exports, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var latest = await exports.LatestAsync(userId, ct);
        return latest is null ? NoContent() : Ok(latest);
    }

    /// <summary>The archive, to its owner, within 7 days of being made.</summary>
    [HttpGet("export/{id:guid}/download")]
    public async Task<IActionResult> DownloadExport(Guid id, [FromServices] AccountExportService exports, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var file = await exports.OpenAsync(userId, id, ct);
        return file is null ? NotFound() : File(file.Value.Stream, "application/zip", file.Value.FileName);
    }
}
