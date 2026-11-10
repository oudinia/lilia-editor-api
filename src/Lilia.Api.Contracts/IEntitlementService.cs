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

    /// <summary>Whether plan limits are enforced (<c>Entitlements:Enforce</c>).</summary>
    bool Enforced { get; }

    /// <summary>Refuse a new document when enforcement is on and the plan's documents are used.</summary>
    Task EnsureCanCreateDocumentAsync(string userId, CancellationToken ct = default);

    /// <summary>Refuse an import (which also creates a document) when enforcement is on and either limit is reached.</summary>
    Task EnsureCanImportAsync(string userId, CancellationToken ct = default);

    /// <summary>True when the user's plan caps documents (Free); false for unlimited plans.</summary>
    Task<bool> HasFiniteDocumentLimitAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// The owner's documents that are read-only because their plan holds fewer than they own after a downgrade (launch
    /// redesign 4d). Empty unless limits are enforced, the owner isn't staff, once held a paid plan and is over.
    /// </summary>
    Task<IReadOnlySet<Guid>> PlanLockedDocumentsAsync(string ownerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

    /// <summary>Choose which documents stay editable over the limit (4e): exactly as many as the plan holds, once a day.</summary>
    Task<ChooseEditableResult> ChooseEditableAsync(string ownerId, IReadOnlyList<Guid> documentIds, CancellationToken ct = default) =>
        Task.FromResult(new ChooseEditableResult("not_over_limit"));

    /// <summary>The plan and how much of it is used, for <c>GET /api/plan</c>.</summary>
    Task<PlanUsageDto?> GetUsageAsync(string userId, CancellationToken ct = default);

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

    /// <summary>
    /// The same, with the prompt cache priced in (3 Nov 2026): cache writes at 1.25× the input rate, reads at
    /// 0.1×. Without its own version, an implementation counts every input token at the full rate.
    /// No <paramref name="aiRequestId"/> for the editor's quick AI actions, which keep no audit row (4 Nov 2026).
    /// </summary>
    Task<int> RecordAiSpendAsync(string userId, string modelId, AiTokenUse use, Guid? aiRequestId, CancellationToken ct = default) =>
        RecordAiSpendAsync(userId, modelId, use.Input, use.Output, aiRequestId ?? Guid.Empty, ct);

    /// <summary>
    /// Append a flat, pre-computed credit surcharge for the same request — for
    /// server-side tool use (e.g. web search) that bills outside the per-token
    /// model. No-op when <paramref name="credits"/> &lt;= 0. Recorded as 'spend'
    /// so it draws down the balance alongside the token spend.
    /// </summary>
    Task RecordAiSurchargeAsync(string userId, int credits, string note, Guid aiRequestId, CancellationToken ct = default);

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

/// <summary>A limit: <see cref="Limit"/> -1 means unlimited.</summary>
public record PlanMeterDto(int Used, int Limit, DateTime? ResetsAt);

/// <summary>
/// How far through the month's allowance (0–1), with no counts: Ask Lilia is never a balance.
/// <see cref="Enforced"/>: whether a used allowance stops Ask Lilia (<c>AI:EnforceCredits</c>).
/// </summary>
/// <param name="ComingSoon">Ask Lilia is "coming soon" (AI:ComingSoon): the panel says so instead of asking.</param>
public record PlanAllowanceDto(double Fraction, bool Unlimited, DateTime ResetsAt, bool Enforced = false, bool ComingSoon = false);

public record PlanNameDto(string Slug, string Name);

public record PlanUsageDto(bool Enforced, PlanNameDto Plan, PlanMeterDto Documents, PlanMeterDto Imports, PlanAllowanceDto Ask,
    PlanReadOnlyDto? ReadOnly = null);

/// <summary>
/// Over the plan's limit after a downgrade (launch redesign 4d/4e): how many documents are read-only, how many stay
/// editable, and when the author may choose them again (null: now). Null on the plan when nothing is read-only.
/// </summary>
public record PlanReadOnlyDto(int Locked, int Editable, DateTime? ChooseAgainAt);

/// <summary>The outcome of choosing which documents stay editable: null error when saved.</summary>
public record ChooseEditableResult(string? Error, string? Message = null, DateTime? RetryAt = null);

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
