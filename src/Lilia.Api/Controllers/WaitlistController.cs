using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

// =====================================================================
//  Waitlist API.
//
//  POST /api/waitlist/companion-app
//    Anonymous — anyone can sign up. Validates email shape, dedupes
//    by LOWER(email) via the partial unique index.
//
//  GET  /api/waitlist/companion-app/count
//    Public — exposes the total signed-up count (no PII) so the
//    marketing site can show "Join N people waiting" once volume is
//    reasonable.
//
//  Source of truth: lilia-docs/launch-readiness/
//  2026-05-18-mobile-companion-postponed.md §6.
// =====================================================================

[ApiController]
[Route("api/waitlist")]
public class WaitlistController : ControllerBase
{
    private readonly LiliaDbContext _db;
    private readonly ILogger<WaitlistController> _logger;

    public WaitlistController(LiliaDbContext db, ILogger<WaitlistController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("companion-app")]
    [AllowAnonymous]
    public async Task<IActionResult> Signup(
        [FromBody] CompanionAppSignupRequest req,
        CancellationToken ct)
    {
        var email = req.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !IsLikelyEmail(email))
        {
            return BadRequest(new { error = "invalid_email" });
        }
        if (email.Length > 320)
        {
            return BadRequest(new { error = "email_too_long" });
        }

        // Dedupe by LOWER(email). The unique partial index ignores
        // unsubscribed rows so a previously-unsubscribed user can
        // re-subscribe.
        var existing = await _db.CompanionAppWaitlist
            .Where(w => w.Email.ToLower() == email.ToLower() && w.UnsubscribedAt == null)
            .Select(w => w.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty)
        {
            // Idempotent — return success even if already on the list,
            // so the frontend's "submitted" state behaves predictably.
            return Ok(new { added = false, message = "already_subscribed" });
        }

        var row = new CompanionAppWaitlist
        {
            UserId = User.FindFirst("sub")?.Value
                     ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Email = email,
            Locale = (req.Locale ?? "en").ToLowerInvariant(),
            UserAgent = Request.Headers.UserAgent.ToString().Length > 500
                ? Request.Headers.UserAgent.ToString()[..500]
                : Request.Headers.UserAgent.ToString(),
            Source = req.Source ?? "banner",
            SignedUpAt = DateTime.UtcNow,
        };
        _db.CompanionAppWaitlist.Add(row);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Likely a partial-index collision raced past the SELECT
            // above. Idempotent: treat as already-on-list.
            _logger.LogInformation(ex, "Waitlist dedupe race for {Email}", email);
            return Ok(new { added = false, message = "already_subscribed" });
        }

        _logger.LogInformation(
            "Companion-app waitlist signup: {Email} (source={Source}, locale={Locale}, signed_in={SignedIn})",
            email, row.Source, row.Locale, row.UserId is not null);
        return Ok(new { added = true });
    }

    [HttpGet("companion-app/count")]
    [AllowAnonymous]
    public async Task<IActionResult> Count(CancellationToken ct)
    {
        var count = await _db.CompanionAppWaitlist
            .CountAsync(w => w.UnsubscribedAt == null, ct);
        return Ok(new { count });
    }

    private static bool IsLikelyEmail(string s)
    {
        // Conservative shape check. Real validation is "did the
        // confirmation email send?" — but that's a v2 concern; for
        // launch a regex is fine.
        var at = s.IndexOf('@');
        var dot = s.LastIndexOf('.');
        return at > 0 && dot > at + 1 && dot < s.Length - 1;
    }
}

public class CompanionAppSignupRequest
{
    public string? Email { get; set; }
    public string? Locale { get; set; }
    public string? Source { get; set; }
}
