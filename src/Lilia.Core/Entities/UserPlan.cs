namespace Lilia.Core.Entities;

/// <summary>
/// A user's active (or past) subscription to a plan. One row per
/// (user, plan, activation). `Status = 'active'` rows should be unique
/// per user — enforced by a unique partial index.
///
/// Fields mirror the common Stripe / Lemonsqueezy subscription model:
///   - CurrentPeriodStart / End drive quota-window resets.
///   - ExternalRef holds the provider's subscription id for reconciliation.
///   - CancelAtPeriodEnd is set when the user cancels but still has
///     access through the current period.
/// </summary>
public class UserPlan
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid PlanId { get; set; }

    // active | trial | past_due | cancelled
    public string Status { get; set; } = "active";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAt { get; set; }

    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }

    public string? ExternalRef { get; set; }   // e.g. Stripe sub_123

    public bool CancelAtPeriodEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
    public virtual Plan? Plan { get; set; }
}
