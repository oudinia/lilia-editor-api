using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

    public EntitlementService(LiliaDbContext context, ILogger<EntitlementService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ActivePlanDto?> GetActivePlanAsync(string userId, CancellationToken ct = default)
    {
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
        var plan = await GetActivePlanAsync(userId, ct);
        if (plan is null) return; // No plan data at all — fail open (logged elsewhere).

        var (capKey, used, resetsAt) = resource switch
        {
            QuotaResource.Documents         => ("maxDocs",           await CountDocsAsync(userId, ct),         (DateTime?)null),
            QuotaResource.ImportsPerMonth   => ("maxImportsPerMonth", await CountImportsThisMonthAsync(userId, ct), NextMonthStart()),
            QuotaResource.AiCredits         => ("aiCreditsPerMonth", -await GetAiCreditBalanceAsync(userId, ct), plan.CurrentPeriodEnd),
            QuotaResource.TeamSeats         => ("maxTeamSeats",      await CountTeamSeatsAsync(userId, ct),    (DateTime?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };

        var cap = ReadCap(plan.Caps, capKey);
        if (cap < 0) return;                          // -1 = unlimited
        if (used + delta > cap)
            throw new QuotaExceededException(capKey, cap, used, resetsAt);
    }

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

    // ── Helpers ──────────────────────────────────────────────────────

    private Task<int> CountDocsAsync(string userId, CancellationToken ct) =>
        _context.Documents.AsNoTracking().CountAsync(d => d.OwnerId == userId, ct);

    private Task<int> CountImportsThisMonthAsync(string userId, CancellationToken ct)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return _context.Jobs.AsNoTracking()
            .CountAsync(j => j.UserId == userId && j.JobType == "IMPORT" && j.CreatedAt >= monthStart, ct);
    }

    private Task<int> CountTeamSeatsAsync(string userId, CancellationToken ct) =>
        _context.Teams.AsNoTracking()
            .Where(t => t.OwnerId == userId)
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
