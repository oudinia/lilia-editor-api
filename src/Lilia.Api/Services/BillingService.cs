using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
// Stripe.Checkout.Session collides with Lilia.Core.Entities.Session (auth).
using StripeCheckoutSession = Stripe.Checkout.Session;

namespace Lilia.Api.Services;

// =====================================================================
//  Centralized membership billing. The C# API owns the Stripe
//  integration end-to-end: Checkout, the customer portal, and the
//  subscription webhook. Stripe events drive the UserPlan table, which
//  EntitlementService already reads for quota / feature gating.
//
//  Design notes:
//   - One UserPlan row per user, updated in place. The "active row is
//     unique per user" partial index forbids a second active row, so we
//     never insert a duplicate — we upsert.
//   - Webhook dedup: Stripe delivers at-least-once. stripe_events has a
//     UNIQUE(stripe_event_id); a row with ProcessedAt set = already
//     handled, skip. A row without it = a prior attempt failed, retry.
//   - Stripe is the upstream source of truth; our tables mirror it.
// =====================================================================

public interface IBillingService
{
    /// <summary>Create a Stripe Checkout Session for a plan; returns the redirect URL.</summary>
    Task<string> CreateCheckoutSessionAsync(
        string userId, string planSlug, string billingInterval, CancellationToken ct = default);

    /// <summary>Create a Stripe billing-portal session for the user; returns the URL.</summary>
    Task<string> CreatePortalSessionAsync(string userId, CancellationToken ct = default);

    /// <summary>Verify + process a Stripe webhook payload. Idempotent.</summary>
    Task HandleWebhookAsync(string payload, string signatureHeader, CancellationToken ct = default);
}

/// <summary>Billing can't proceed — not configured, bad request, or signature failure.</summary>
public class BillingException(string message) : Exception(message);

public class BillingService : IBillingService
{
    private readonly LiliaDbContext _db;
    private readonly StripeOptions _opts;
    private readonly ILogger<BillingService> _logger;

    public BillingService(LiliaDbContext db, IOptions<StripeOptions> opts, ILogger<BillingService> logger)
    {
        _db = db;
        _opts = opts.Value;
        _logger = logger;
    }

    // ── Checkout ──────────────────────────────────────────────────────

    public async Task<string> CreateCheckoutSessionAsync(
        string userId, string planSlug, string billingInterval, CancellationToken ct = default)
    {
        RequireConfigured();

        var plan = await _db.Plans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == planSlug && p.IsActive, ct)
            ?? throw new BillingException($"Unknown or inactive plan '{planSlug}'.");

        var yearly = string.Equals(billingInterval, "year", StringComparison.OrdinalIgnoreCase);
        var priceId = yearly ? plan.StripeYearlyPriceId : plan.StripeMonthlyPriceId;
        if (string.IsNullOrEmpty(priceId))
            throw new BillingException(
                $"Plan '{planSlug}' has no Stripe {(yearly ? "yearly" : "monthly")} price configured.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BillingException("User not found.");

        var customerId = await EnsureStripeCustomerAsync(user, ct);

        var metadata = new Dictionary<string, string>
        {
            ["user_id"] = userId,
            ["plan_slug"] = planSlug,
        };
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            ClientReferenceId = userId,
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata },
            Metadata = metadata,
            SuccessUrl = _opts.CheckoutSuccessUrl,
            CancelUrl = _opts.CheckoutCancelUrl,
            AllowPromotionCodes = true,
        };

        var session = await new SessionService().CreateAsync(options, RequestOpts(), ct);
        _logger.LogInformation(
            "Stripe checkout session {SessionId} created — user {UserId}, plan {Plan}/{Interval}",
            session.Id, userId, planSlug, yearly ? "year" : "month");
        return session.Url;
    }

    // ── Customer portal ───────────────────────────────────────────────

    public async Task<string> CreatePortalSessionAsync(string userId, CancellationToken ct = default)
    {
        RequireConfigured();

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BillingException("User not found.");
        if (string.IsNullOrEmpty(user.PaymentsCustomerId))
            throw new BillingException("No billing account for this user yet.");

        var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = user.PaymentsCustomerId,
                ReturnUrl = _opts.PortalReturnUrl,
            },
            RequestOpts(), ct);
        return session.Url;
    }

    // ── Webhook ───────────────────────────────────────────────────────

    public async Task HandleWebhookAsync(string payload, string signatureHeader, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opts.WebhookSecret))
            throw new BillingException("Stripe webhook secret not configured.");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _opts.WebhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed");
            throw new BillingException("Webhook signature verification failed.");
        }

        // Dedup. A processed row = Stripe re-delivery, skip. An unprocessed
        // row = a previous attempt failed, retry it. No row = first sight.
        var ledger = await _db.StripeEvents
            .FirstOrDefaultAsync(e => e.StripeEventId == stripeEvent.Id, ct);
        if (ledger is { ProcessedAt: not null })
        {
            _logger.LogDebug("Stripe webhook {EventId} ({Type}) already processed — skip",
                stripeEvent.Id, stripeEvent.Type);
            return;
        }
        if (ledger is null)
        {
            ledger = new StripeEvent { StripeEventId = stripeEvent.Id, EventType = stripeEvent.Type };
            _db.StripeEvents.Add(ledger);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // UNIQUE(stripe_event_id) violation — a concurrent
                // delivery won the race. That instance owns processing.
                _logger.LogDebug("Stripe webhook {EventId}: concurrent delivery — skip", stripeEvent.Id);
                return;
            }
        }
        else
        {
            ledger.Error = null; // clear the prior failure before retry
        }

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompletedAsync((StripeCheckoutSession)stripeEvent.Data.Object, ct);
                    break;
                case "customer.subscription.created":
                case "customer.subscription.updated":
                    await UpsertSubscriptionAsync((Subscription)stripeEvent.Data.Object, ct);
                    break;
                case "customer.subscription.deleted":
                    await CancelSubscriptionAsync((Subscription)stripeEvent.Data.Object, ct);
                    break;
                default:
                    _logger.LogDebug("Stripe webhook: ignoring event type {Type}", stripeEvent.Type);
                    break;
            }
            ledger.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe webhook handler failed for {EventId} ({Type})",
                stripeEvent.Id, stripeEvent.Type);
            ledger.Error = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw; // controller returns 500 so Stripe retries the delivery
        }
    }

    // ── Webhook handlers ──────────────────────────────────────────────

    private async Task HandleCheckoutCompletedAsync(StripeCheckoutSession session, CancellationToken ct)
    {
        // Subscription-mode checkout: the subscription itself arrives via
        // customer.subscription.created (which does the plan assignment).
        // Here we just make sure the Stripe customer id is on the user.
        if (string.IsNullOrEmpty(session.SubscriptionId)) return;
        var userId = session.Metadata?.GetValueOrDefault("user_id") ?? session.ClientReferenceId;
        if (string.IsNullOrEmpty(userId)) return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null && string.IsNullOrEmpty(user.PaymentsCustomerId)
            && !string.IsNullOrEmpty(session.CustomerId))
        {
            user.PaymentsCustomerId = session.CustomerId;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task UpsertSubscriptionAsync(Subscription sub, CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(sub, ct);
        if (userId is null)
        {
            _logger.LogWarning("Stripe subscription {SubId}: cannot resolve a Lilia user — skipped", sub.Id);
            return;
        }

        var item = sub.Items?.Data?.FirstOrDefault();
        var priceId = item?.Price?.Id;
        var plan = priceId is null ? null : await _db.Plans.FirstOrDefaultAsync(
            p => p.StripeMonthlyPriceId == priceId || p.StripeYearlyPriceId == priceId, ct);
        if (plan is null)
        {
            _logger.LogWarning("Stripe subscription {SubId}: price {Price} maps to no Plan — skipped",
                sub.Id, priceId ?? "(none)");
            return;
        }

        // One UserPlan row per user, updated in place.
        var userPlan = await _db.UserPlans.FirstOrDefaultAsync(up => up.UserId == userId, ct);
        if (userPlan is null)
        {
            userPlan = new UserPlan { Id = Guid.NewGuid(), UserId = userId, StartedAt = DateTime.UtcNow };
            _db.UserPlans.Add(userPlan);
        }
        userPlan.PlanId = plan.Id;
        userPlan.Status = MapStatus(sub.Status);
        userPlan.ExternalRef = sub.Id;
        userPlan.CurrentPeriodStart = item?.CurrentPeriodStart;
        userPlan.CurrentPeriodEnd = item?.CurrentPeriodEnd;
        userPlan.CancelAtPeriodEnd = sub.CancelAtPeriodEnd;
        userPlan.EndsAt = sub.CancelAtPeriodEnd ? item?.CurrentPeriodEnd : null;
        userPlan.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("UserPlan upserted — user {UserId} → {Plan} ({Status})",
            userId, plan.Slug, userPlan.Status);
    }

    private async Task CancelSubscriptionAsync(Subscription sub, CancellationToken ct)
    {
        var userPlan = await _db.UserPlans.FirstOrDefaultAsync(up => up.ExternalRef == sub.Id, ct);
        if (userPlan is null) return;
        userPlan.Status = "cancelled";
        userPlan.EndsAt = sub.EndedAt ?? sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
        userPlan.CancelAtPeriodEnd = false;
        userPlan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("UserPlan cancelled — user {UserId} (sub {SubId})", userPlan.UserId, sub.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private async Task<string?> ResolveUserIdAsync(Subscription sub, CancellationToken ct)
    {
        // Prefer the metadata stamped at checkout; fall back to the
        // customer-id mapping on the User row.
        var metaUser = sub.Metadata?.GetValueOrDefault("user_id");
        if (!string.IsNullOrEmpty(metaUser)) return metaUser;
        if (string.IsNullOrEmpty(sub.CustomerId)) return null;
        return await _db.Users.Where(u => u.PaymentsCustomerId == sub.CustomerId)
            .Select(u => u.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<string> EnsureStripeCustomerAsync(User user, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(user.PaymentsCustomerId)) return user.PaymentsCustomerId;

        var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
        {
            Email = user.Email,
            Name = user.Name,
            Metadata = new Dictionary<string, string> { ["user_id"] = user.Id },
        }, RequestOpts(), ct);

        user.PaymentsCustomerId = customer.Id;
        await _db.SaveChangesAsync(ct);
        return customer.Id;
    }

    /// <summary>Map a Stripe subscription status to a UserPlan.Status value.</summary>
    private static string MapStatus(string stripeStatus) => stripeStatus switch
    {
        "active" => "active",
        "trialing" => "trial",
        "past_due" => "past_due",
        "unpaid" => "past_due",
        "incomplete" => "past_due",
        "canceled" => "cancelled",
        "incomplete_expired" => "cancelled",
        _ => "active",
    };

    private RequestOptions RequestOpts() => new() { ApiKey = _opts.SecretKey };

    private void RequireConfigured()
    {
        if (!_opts.IsConfigured)
            throw new BillingException("Billing is not configured (Stripe:SecretKey missing).");
    }
}
