using System.Text;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

// =====================================================================
//  Membership billing endpoints. The C# API is the single Stripe
//  integration point — any storefront (lilia-cloud, editor, …) calls
//  these instead of talking to Stripe itself.
//
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
    private readonly ILogger<BillingController> _logger;

    public BillingController(IBillingService billing, ILogger<BillingController> logger)
    {
        _billing = billing;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

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
