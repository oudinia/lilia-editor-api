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

    public record LegalAcceptanceDto(int? Version, DateTime? AcceptedAt);
    public record AcceptLegalRequest(int Version);

    /// <summary>Which version of the Terms the author last accepted (null: none recorded yet).</summary>
    [HttpGet("legal")]
    public async Task<ActionResult<LegalAcceptanceDto>> GetLegal([FromServices] LiliaDbContext db, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? NotFound() : new LegalAcceptanceDto(user.LegalAcceptedVersion, user.LegalAcceptedAt);
    }

    /// <summary>
    /// Record that the author accepted this version of the Terms: at sign-up (the terms line) or with
    /// OK on the notice that they changed (launch redesign 2c). A version is never lowered, so an old
    /// tab can't undo a newer acceptance; the date is the first acceptance of that version.
    /// </summary>
    [HttpPost("legal")]
    public async Task<ActionResult<LegalAcceptanceDto>> AcceptLegal([FromBody] AcceptLegalRequest req, [FromServices] LiliaDbContext db, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (req.Version is < 1 or > 999)
            return BadRequest(new { code = "VALIDATION_FAILED", message = "A Terms version is 1 to 999." });
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();
        if (user.LegalAcceptedVersion is not { } accepted || req.Version > accepted)
        {
            user.LegalAcceptedVersion = req.Version;
            user.LegalAcceptedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return new LegalAcceptanceDto(user.LegalAcceptedVersion, user.LegalAcceptedAt);
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

    public record DeleteAccountRequest(string? ConfirmEmail);

    /// <summary>What deleting would take with it (documents, shared ones, people), and whether it's asked.</summary>
    [HttpGet("account")]
    public async Task<ActionResult<AccountStatusDto>> AccountStatus([FromServices] AccountDeletionService accounts, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var status = await accounts.StatusAsync(userId, ct);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>
    /// Delete the account: type its email, from a recent sign-in. Closed at once, erased in 30 days
    /// unless restored (launch redesign 3g).
    /// </summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest req, [FromServices] AccountDeletionService accounts,
        [FromServices] IConfiguration config, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var recent = AccountDeletionService.RecentlySignedIn(User, DateTime.UtcNow,
            Lilia.Api.Middleware.DevelopmentAuthMiddleware.IsEnabled(config, env.IsDevelopment()));
        var (refusal, erasedOn) = await accounts.RequestAsync(userId, req.ConfirmEmail, recent, ct);
        return refusal switch
        {
            DeletionRefusal.None or DeletionRefusal.AlreadyRequested => Ok(new { erasedOn }),
            DeletionRefusal.EmailMismatch => BadRequest(new { code = "EMAIL_MISMATCH", message = "Type the email of this account to delete it." }),
            _ => StatusCode(StatusCodes.Status403Forbidden, new { code = "REAUTH_REQUIRED", message = "Sign in again to delete your account." }),
        };
    }

    /// <summary>Undo a deletion within its 30 days.</summary>
    [HttpPost("restore")]
    public async Task<IActionResult> RestoreAccount([FromServices] AccountDeletionService accounts, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return await accounts.RestoreAsync(userId, ct) ? NoContent() : NotFound();
    }

    /// <summary>Two-step sign-in on or off, and whether sessions can be listed (Settings → Security).</summary>
    [HttpGet("security")]
    public async Task<ActionResult<SecurityStatusDto>> Security([FromServices] AccountSecurityService security, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await security.StatusAsync(userId, ct));
    }

    public record TwoStepRequest(bool Enabled);

    /// <summary>
    /// Turn two-step sign-in on (from a session that has just used the authenticator: enrolment) or
    /// off (from a session that used it). The authenticator itself is enrolled and removed with Stytch.
    /// </summary>
    [HttpPut("two-step")]
    public async Task<IActionResult> SetTwoStep([FromBody] TwoStepRequest req, [FromServices] AccountSecurityService security, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return await security.SetAsync(userId, User, req.Enabled, DateTime.UtcNow, ct)
            ? NoContent()
            : StatusCode(StatusCodes.Status403Forbidden, new { code = "TWO_STEP_CODE_REQUIRED", message = "Enter a code from your authenticator app first." });
    }

    /// <summary>The author's signed-in sessions, this one first. 204 when sessions aren't available here.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromServices] AccountSecurityService security, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var list = await security.SessionsAsync(userId, AccountSecurityService.SessionId(User), ct);
        return list is null ? NoContent() : Ok(list);
    }

    /// <summary>Sign out one session (by id), or every other one (no id).</summary>
    [HttpPost("sessions/revoke")]
    public async Task<IActionResult> RevokeSessions([FromQuery] string? id, [FromServices] AccountSecurityService security, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!security.SessionsAvailable) return NotFound();
        return Ok(new { signedOut = await security.RevokeAsync(userId, AccountSecurityService.SessionId(User), id, ct) });
    }
}
