using System.Text.Json;
using Lilia.Core.Entities;

namespace Lilia.Api.Services;

/// <summary>
/// The single gateway for "is this user allowed to do X?" questions.
/// Every paid-feature code path should go through this, not read Plan
/// or UserPlan directly.
///
/// v1 scope:
///   - Resolve active plan (with caps + features).
///   - Quota check: doc count, imports this month, AI credits.
///   - Feature check: does this user's plan grant feature X?
///
/// Not yet (future):
///   - Grant credits on period rollover (background job).
///   - Proration on mid-cycle plan change.
/// </summary>
public interface IEntitlementService
{
    /// <summary>Fetch the user's active plan + caps + features.</summary>
    Task<ActivePlanDto?> GetActivePlanAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Throw <see cref="QuotaExceededException"/> if the user would be over
    /// quota for the named resource. Called BEFORE creating the resource.
    /// </summary>
    Task EnsureQuotaAsync(string userId, QuotaResource resource, int delta = 1, CancellationToken ct = default);

    /// <summary>True if the user's active plan grants the feature key.</summary>
    Task<bool> HasFeatureAsync(string userId, string featureKey, CancellationToken ct = default);

    /// <summary>Current AI credit balance (sum of ledger deltas).</summary>
    Task<int> GetAiCreditBalanceAsync(string userId, CancellationToken ct = default);

    /// <summary>Append a spend row to the credit ledger. Called on AiRequest completion.</summary>
    Task RecordAiSpendAsync(string userId, int tokensUsed, Guid aiRequestId, CancellationToken ct = default);

    /// <summary>
    /// Append a model-weighted spend row (credits scaled by the model's catalog
    /// rate — Opus costs ~5x Sonnet for the same tokens). Returns credits spent.
    /// </summary>
    Task<int> RecordAiSpendAsync(string userId, string modelId, int inputTokens, int outputTokens, Guid aiRequestId, CancellationToken ct = default);

    /// <summary>Total credits consumed (sum of spend) for a user — for the UI usage readout.</summary>
    Task<int> GetAiCreditsConsumedAsync(string userId, CancellationToken ct = default);
}

public enum QuotaResource
{
    Documents,
    ImportsPerMonth,
    AiCredits,
    TeamSeats,
}

public record ActivePlanDto(
    Guid PlanId,
    string Slug,
    string DisplayName,
    JsonElement Caps,
    JsonElement Features,
    string Status,            // active | trial | past_due | cancelled
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public class QuotaExceededException : Exception
{
    public string Resource { get; }
    public int Cap { get; }
    public int Used { get; }
    public DateTime? ResetsAt { get; }

    public QuotaExceededException(string resource, int cap, int used, DateTime? resetsAt, string? message = null)
        : base(message ?? $"Quota exceeded for {resource}: {used}/{cap}")
    {
        Resource = resource;
        Cap = cap;
        Used = used;
        ResetsAt = resetsAt;
    }
}
