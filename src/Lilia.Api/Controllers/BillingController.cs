using System.Text;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

// =====================================================================
//  Membership billing endpoints. The C# API is the single Stripe
//  integration point — any storefront (lilia-cloud, editor, …) calls
//  these instead of talking to Stripe itself.
//
//    GET  /api/billing/plans     — public catalog of plans (anon)
//    POST /api/billing/checkout  — start a Checkout for a plan (auth)
//    POST /api/billing/portal    — open the Stripe customer portal (auth)
//    POST /api/billing/webhook   — Stripe subscription webhook (anon,
//                                  authenticated by the Stripe signature)
// =====================================================================

[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billing;
    private readonly LiliaDbContext _db;
    private readonly ILogger<BillingController> _logger;

    public BillingController(IBillingService billing, LiliaDbContext db, ILogger<BillingController> logger)
    {
        _billing = billing;
        _db = db;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Public catalog of active plans. Storefront pricing tables and
    /// in-app upgrade prompts call this to render plan cards. Includes
    /// caps + features so the same response drives the marketing page
    /// and the entitlement-aware UI on the editor side. No PII; safe to
    /// cache for ~5 min.
    /// </summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        var plans = await _db.Plans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPrice ?? 0m)
            .Select(p => new
            {
                id = p.Id,
                slug = p.Slug,
                displayName = p.DisplayName,
                monthlyPrice = p.MonthlyPrice,
                yearlyPrice = p.YearlyPrice,
                caps = p.Caps,
                features = p.Features,
            })
            .ToListAsync(ct);

        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(new { plans });
    }

    public record CheckoutRequest(string PlanSlug, string? Interval);

    /// <summary>Start a Stripe Checkout for a membership plan.</summary>
    [HttpPost("checkout")]
    [Authorize]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest req, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.PlanSlug))
            return BadRequest(new { error = "plan_slug_required" });

        try
        {
            var url = await _billing.CreateCheckoutSessionAsync(
                userId, req.PlanSlug, req.Interval ?? "month", ct);
            return Ok(new { url });
        }
        catch (BillingException ex)
        {
            return StatusCode(503, new { error = "billing_unavailable", message = ex.Message });
        }
    }

    /// <summary>Open the Stripe customer portal to manage the membership.</summary>
    [HttpPost("portal")]
    [Authorize]
    public async Task<IActionResult> Portal(CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var url = await _billing.CreatePortalSessionAsync(userId, ct);
            return Ok(new { url });
        }
        catch (BillingException ex)
        {
            return StatusCode(503, new { error = "billing_unavailable", message = ex.Message });
        }
    }

    /// <summary>
    /// Stripe subscription webhook. Anonymous — authenticated by the
    /// Stripe-Signature header, verified inside the billing service.
    /// 200 = handled / duplicate (Stripe stops retrying); 400 = signature
    /// failure; 500 = handler error (Stripe retries the delivery).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        // Re-read the raw body — the Stripe signature is over exact bytes.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["Stripe-Signature"].ToString();
        try
        {
            await _billing.HandleWebhookAsync(payload, signature, ct);
            return Ok(new { received = true });
        }
        catch (BillingException ex)
        {
            // Signature / config rejection — let Stripe retry.
            _logger.LogWarning("Stripe webhook rejected: {Message}", ex.Message);
            return BadRequest(new { error = "webhook_rejected" });
        }
        catch (Exception ex)
        {
            // Handler error — already logged + recorded on the stripe_events
            // row. 500 so Stripe retries the delivery.
            _logger.LogError(ex, "Stripe webhook handler error");
            return StatusCode(500, new { error = "handler_error" });
        }
    }
}
