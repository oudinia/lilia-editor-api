using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

/// <summary>
/// Plan / quota / credit enforcement. Fast-path: a single query resolves
/// the active plan + caps, counts for quota checks are aggregates
/// against existing tables (documents, jobs, ai_requests, ai_credit_ledger).
/// No new usage table.
///
/// Users without a UserPlan row are implicitly on the "free" plan — the
/// service returns the free plan's caps rather than failing.
/// </summary>
public class EntitlementService : IEntitlementService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<EntitlementService> _logger;
    private readonly IAiCatalogService _aiCatalog;
    private readonly EntitlementOptions _options;
    private readonly bool _aiEnforced;

    public EntitlementService(LiliaDbContext context, ILogger<EntitlementService> logger, IAiCatalogService aiCatalog,
        IOptions<EntitlementOptions>? options = null, IConfiguration? configuration = null)
    {
        // The same switch AskLiliaService and AiArchitectService read before refusing a request.
        _aiEnforced = configuration?.GetValue("AI:EnforceCredits", false) ?? false;
        _context = context;
        _logger = logger;
        _aiCatalog = aiCatalog;
        _options = options?.Value ?? new EntitlementOptions();
    }

    public bool Enforced => _options.Enforce;

    private bool IsStaff(string userId) => _options.StaffUserIds.Contains(userId, StringComparer.Ordinal);

    public async Task<ActivePlanDto?> GetActivePlanAsync(string userId, CancellationToken ct = default)
    {
        if (IsStaff(userId))
        {
            var beta = await _context.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == "beta" && p.IsActive, ct);
            if (beta is not null)
                return new ActivePlanDto(beta.Id, beta.Slug, beta.DisplayName, beta.Caps.RootElement, beta.Features.RootElement,
                    Status: "active", CurrentPeriodEnd: null, CancelAtPeriodEnd: false);
        }

        var userPlan = await _context.UserPlans
            .AsNoTracking()
            .Include(up => up.Plan)
            .Where(up => up.UserId == userId && up.Status == "active")
            .OrderByDescending(up => up.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (userPlan is null || userPlan.Plan is null)
        {
            // Fall back to the free plan. This lets call sites always
            // receive an ActivePlanDto without special-casing "no row".
            var free = await _context.Plans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Slug == "free" && p.IsActive, ct);
            if (free is null) return null;
            return new ActivePlanDto(
                free.Id, free.Slug, free.DisplayName,
                free.Caps.RootElement, free.Features.RootElement,
                Status: "active", CurrentPeriodEnd: null, CancelAtPeriodEnd: false);
        }

        return new ActivePlanDto(
            userPlan.Plan.Id, userPlan.Plan.Slug, userPlan.Plan.DisplayName,
            userPlan.Plan.Caps.RootElement, userPlan.Plan.Features.RootElement,
            userPlan.Status, userPlan.CurrentPeriodEnd, userPlan.CancelAtPeriodEnd);
    }

    public async Task EnsureQuotaAsync(string userId, QuotaResource resource, int delta = 1, CancellationToken ct = default)
    {
        // Staff (the owner's account) hold Beta for its features but no limit at all: not documents,
        // and not Ask Lilia, whose Beta allowance would otherwise stop the owner's own use.
        if (IsStaff(userId)) return;
        var plan = await GetActivePlanAsync(userId, ct);
        if (plan is null) return; // No plan data at all — fail open (logged elsewhere).

        var (capKey, used, resetsAt) = resource switch
        {
            QuotaResource.Documents         => ("maxDocs",           await CountDocsAsync(userId, ct),         (DateTime?)null),
            QuotaResource.ImportsPerMonth   => ("maxImportsPerMonth", await CountImportsThisMonthAsync(userId, ct), NextMonthStart()),
            QuotaResource.AiCredits         => ("aiCreditsPerMonth", await GetAiCreditsSpentThisMonthAsync(userId, ct), NextMonthStart()),
            QuotaResource.TeamSeats         => ("maxTeamSeats",      await CountTeamSeatsAsync(userId, ct),    (DateTime?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };

        var cap = ReadCap(plan.Caps, capKey);
        if (cap < 0) return;                          // -1 = unlimited
        if (used + delta > cap)
            throw new QuotaExceededException(capKey, cap, used, resetsAt, QuotaMessage(capKey, cap, resetsAt));
    }

    public async Task EnsureCanCreateDocumentAsync(string userId, CancellationToken ct = default)
    {
        if (_options.Enforce) await EnsureQuotaAsync(userId, QuotaResource.Documents, 1, ct);
    }

    public async Task EnsureCanImportAsync(string userId, CancellationToken ct = default)
    {
        if (!_options.Enforce) return;
        await EnsureQuotaAsync(userId, QuotaResource.ImportsPerMonth, 1, ct);
        await EnsureQuotaAsync(userId, QuotaResource.Documents, 1, ct);
    }

    public async Task<bool> HasFiniteDocumentLimitAsync(string userId, CancellationToken ct = default)
    {
        var plan = await GetActivePlanAsync(userId, ct);
        return plan is not null && ReadCap(plan.Caps, "maxDocs") >= 0;
    }

    public async Task<PlanUsageDto?> GetUsageAsync(string userId, CancellationToken ct = default)
    {
        var plan = await GetActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var askCap = IsStaff(userId) ? -1 : ReadCap(plan.Caps, "aiCreditsPerMonth");
        var askSpent = await GetAiCreditsSpentThisMonthAsync(userId, ct);
        return new PlanUsageDto(
            Enforced: _options.Enforce,
            Plan: new PlanNameDto(plan.Slug, plan.DisplayName),
            Documents: new PlanMeterDto(await CountDocsAsync(userId, ct), ReadCap(plan.Caps, "maxDocs"), null),
            Imports: new PlanMeterDto(await CountImportsThisMonthAsync(userId, ct), ReadCap(plan.Caps, "maxImportsPerMonth"), NextMonthStart()),
            // Ask Lilia is an allowance, never a balance (design rule): only how far through it you are.
            Ask: new PlanAllowanceDto(
                askCap < 0 ? 0 : askCap == 0 ? 1 : Math.Min(1.0, (double)askSpent / askCap),
                Unlimited: askCap < 0,
                ResetsAt: NextMonthStart(),
                Enforced: _aiEnforced));
    }

    private static string QuotaMessage(string capKey, int cap, DateTime? resetsAt) => capKey switch
    {
        "maxDocs" => $"Your plan holds {cap} documents. Move one to the Trash or upgrade to start another.",
        "maxImportsPerMonth" => $"Your plan includes {cap} imports a month; the next one is available on {resetsAt:d MMMM}.",
        "aiCreditsPerMonth" => $"This month's Ask Lilia allowance is used; it renews on {resetsAt:d MMMM}.",
        _ => $"Your plan's limit for {capKey} is reached.",
    };

    public async Task<bool> HasFeatureAsync(string userId, string featureKey, CancellationToken ct = default)
    {
        var plan = await GetActivePlanAsync(userId, ct);
        if (plan is null) return false;
        if (plan.Features.ValueKind != JsonValueKind.Array) return false;
        foreach (var f in plan.Features.EnumerateArray())
        {
            if (f.ValueKind == JsonValueKind.String && f.GetString() == featureKey) return true;
        }
        return false;
    }

    public async Task<int> GetAiCreditBalanceAsync(string userId, CancellationToken ct = default)
    {
        var sum = await _context.AiCreditLedger
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .SumAsync(l => (int?)l.Delta, ct);
        return sum ?? 0;
    }

    public async Task RecordAiSpendAsync(string userId, int tokensUsed, Guid aiRequestId, CancellationToken ct = default)
    {
        // 1 credit ≈ 1000 tokens. Ceiling so a 1-token call still spends 1.
        var credits = Math.Max(1, (tokensUsed + 999) / 1000);
        _context.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Delta = -credits,
            Reason = "spend",
            AiRequestId = aiRequestId,
            Note = $"{tokensUsed} tokens",
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> RecordAiSpendAsync(string userId, string modelId, int inputTokens, int outputTokens, Guid aiRequestId, CancellationToken ct = default)
    {
        var credits = _aiCatalog.CreditsFor(modelId, inputTokens, outputTokens);
        _context.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Delta = -credits,
            Reason = "spend",
            AiRequestId = aiRequestId,
            Note = $"{modelId}: {inputTokens}+{outputTokens} tok",
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);
        return credits;
    }

    public async Task RecordAiSurchargeAsync(string userId, int credits, string note, Guid aiRequestId, CancellationToken ct = default)
    {
        if (credits <= 0) return;
        note ??= string.Empty;
        _context.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Delta = -credits,
            Reason = "spend",
            AiRequestId = aiRequestId,
            Note = note.Length > 500 ? note[..500] : note,
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Total credits consumed (sum of spend magnitudes) for a user.</summary>
    public async Task<int> GetAiCreditsConsumedAsync(string userId, CancellationToken ct = default)
    {
        var spent = await _context.AiCreditLedger
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Reason == "spend")
            .SumAsync(l => (int?)l.Delta, ct);
        return -(spent ?? 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private Task<int> CountDocsAsync(string userId, CancellationToken ct) =>
        // Documents you own. Not playground sandboxes, templates or help articles; documents in
        // the Trash are filtered out by the query filter, and documents shared with you aren't yours.
        _context.Documents.AsNoTracking().CountAsync(d => d.OwnerId == userId && !d.IsPlayground && !d.IsTemplate && !d.IsHelpContent, ct);

    private async Task<int> GetAiCreditsSpentThisMonthAsync(string userId, CancellationToken ct)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var spent = await _context.AiCreditLedger.AsNoTracking()
            .Where(l => l.UserId == userId && l.Reason == "spend" && l.CreatedAt >= monthStart)
            .SumAsync(l => (int?)l.Delta, ct);
        return -(spent ?? 0);
    }

    private Task<int> CountImportsThisMonthAsync(string userId, CancellationToken ct)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return _context.Jobs.AsNoTracking()
            .CountAsync(j => j.UserId == userId && j.JobType == "IMPORT" && j.CreatedAt >= monthStart, ct);
    }

    private Task<int> CountTeamSeatsAsync(string userId, CancellationToken ct) =>
        _context.Teams.AsNoTracking()
            // FT-SANDBOX-SCOPE: sandbox teams' seats don't count against quota.
            .Where(t => t.OwnerId == userId && !t.IsPlayground)
            .SelectMany(t => t.Groups.SelectMany(g => g.Members))
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(ct);

    private static int ReadCap(JsonElement caps, string key)
    {
        if (caps.ValueKind != JsonValueKind.Object) return 0;
        if (!caps.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    }

    private static DateTime NextMonthStart()
    {
        var now = DateTime.UtcNow;
        var month = now.Month == 12 ? 1 : now.Month + 1;
        var year = now.Month == 12 ? now.Year + 1 : now.Year;
        return new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
