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
    private readonly decimal _siteBudgetUsd;
    private readonly decimal _freeBudgetUsd;
    private readonly bool _aiComingSoon;

    /// <summary>
    /// The connector-first launch (the owner, 10 Nov 2026): Ask Lilia is shown as "coming soon" and Lilia pays for no
    /// AI; authors write with their own AI through the connector. <c>AI:ComingSoon</c> refuses every metered AI call
    /// here, where each one asks for its credits, so no route is missed. Staff keep it, to try it before it returns.
    /// </summary>
    internal const string ComingSoonMessage =
        "Ask Lilia is coming soon. Meanwhile, connect Claude to Lilia: it writes and checks your documents with your own AI.";

    public EntitlementService(LiliaDbContext context, ILogger<EntitlementService> logger, IAiCatalogService aiCatalog,
        Lilia.Api.Directories.ITeamDirectory teams, IOptions<EntitlementOptions>? options = null, IConfiguration? configuration = null)
    {
        _teams = teams;
        // The same switch AskLiliaService and AiArchitectService read before refusing a request.
        _aiEnforced = configuration?.GetValue("AI:EnforceCredits", false) ?? false;
        _siteBudgetUsd = configuration?.GetValue("AI:MonthlyBudgetUsd", 0m) ?? 0m;
        _freeBudgetUsd = configuration?.GetValue("AI:FreeMonthlyBudgetUsd", 0m) ?? 0m;
        _aiComingSoon = configuration?.GetValue("AI:ComingSoon", false) ?? false;
        _pdfSitePages = configuration?.GetValue("Import:PdfSitePagesPerMonth", 2000) ?? 2000;
        _context = context;
        _logger = logger;
        _aiCatalog = aiCatalog;
        _options = options?.Value ?? new EntitlementOptions();
    }

    public bool Enforced => _options.Enforce;

    private readonly int _pdfSitePages;

    internal const string PdfNotInPlanMessage =
        "PDF import is part of Pro: each page is read by a paid service. Upgrade to import PDFs, or import the LaTeX, Word or Markdown version.";

    public async Task EnsureCanImportPdfAsync(string userId, int pages, CancellationToken ct = default)
    {
        if (IsStaff(userId)) return;
        pages = Math.Max(1, pages);
        // Fails closed, unlike the other limits: a PDF costs Lilia money per page.
        var plan = await GetActivePlanAsync(userId, ct);
        var cap = plan is null ? 0 : ReadCap(plan.Caps, "pdfPagesPerMonth");
        if (cap == 0)
            throw new QuotaExceededException("pdfImport", 0, 0, null, PdfNotInPlanMessage);
        var used = await PdfPagesThisMonthAsync(userId, ct);
        if (cap > 0 && used + pages > cap)
            throw new QuotaExceededException("pdfPagesPerMonth", cap, used, NextMonthStart(),
                used >= cap
                    ? $"This month's {cap} PDF pages are used; they renew on {NextMonthStart():d MMMM}."
                    : $"This PDF has {pages} pages and {cap - used} of this month's {cap} PDF pages are left. They renew on {NextMonthStart():d MMMM}.");
        if (_pdfSitePages > 0)
        {
            var start = MonthStart();
            var all = await _context.Operations.AsNoTracking()
                .Where(o => o.Kind == "import.pdf" && o.CreatedAt >= start).SumAsync(o => (int?)o.Units, ct) ?? 0;
            if (all + pages > _pdfSitePages)
            {
                _logger.LogWarning("[Entitlements] PDF import refused: the site's {Cap} pages this month are used ({Used})", _pdfSitePages, all);
                throw new QuotaExceededException("pdfSitePages", _pdfSitePages, all, NextMonthStart(),
                    $"PDF import is paused until {NextMonthStart():d MMMM}: this month's PDF reading is used up. Every other import works as usual.");
            }
        }
    }

    public async Task<int> PdfPagesThisMonthAsync(string userId, CancellationToken ct = default)
    {
        var start = MonthStart();
        return await _context.Operations.AsNoTracking()
            .Where(o => o.UserId == userId && o.Kind == "import.pdf" && o.CreatedAt >= start).SumAsync(o => (int?)o.Units, ct) ?? 0;
    }

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
        if (resource == QuotaResource.AiCredits && _aiComingSoon)
            throw new QuotaExceededException("aiComingSoon", 0, 0, null, ComingSoonMessage);
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
        if (cap >= 0 && used + delta > cap)           // -1 = unlimited
            throw new QuotaExceededException(capKey, cap, used, resetsAt, QuotaMessage(capKey, cap, resetsAt));
        if (resource == QuotaResource.AiCredits)
            await EnsureSiteAiBudgetAsync(plan.Slug, ct);
    }

    /// <summary>
    /// The site's monthly AI budget (4 Nov 2026). Lilia launches on a fixed prepaid amount of AI, paid
    /// before any subscription pays out, so this month's spend for everyone stops at
    /// <c>AI:MonthlyBudgetUsd</c>, and free accounts' share of it at <c>AI:FreeMonthlyBudgetUsd</c>: free use
    /// can't eat what paying authors need. 0 or unset is no cap. Spend is the ledger's credits at
    /// <see cref="AiArchitectPricing.UsdPerCredit"/>, which track the model prices. Staff never reach here.
    /// </summary>
    private async Task EnsureSiteAiBudgetAsync(string planSlug, CancellationToken ct)
    {
        if (_siteBudgetUsd <= 0 && _freeBudgetUsd <= 0) return;
        var monthStart = MonthStart();
        var spends = _context.AiCreditLedger.AsNoTracking().Where(l => l.Reason == "spend" && l.CreatedAt >= monthStart);

        if (_siteBudgetUsd > 0)
        {
            var all = -(await spends.SumAsync(l => (int?)l.Delta, ct) ?? 0);
            if (AiArchitectPricing.CreditsToUsd(all) >= _siteBudgetUsd)
                throw new QuotaExceededException("aiSiteBudget", AiArchitectPricing.UsdToCredits(_siteBudgetUsd), all, NextMonthStart(),
                    QuotaMessage("aiSiteBudget", 0, NextMonthStart()));
        }

        if (_freeBudgetUsd > 0 && planSlug == "free")
        {
            // Free is "no active paid plan": the same rule GetActivePlanAsync falls back on.
            var paid = _context.UserPlans.Where(p => p.Status == "active" && p.Plan!.Slug != "free").Select(p => p.UserId);
            var staff = _options.StaffUserIds;
            var free = -(await spends.Where(l => !paid.Contains(l.UserId) && !staff.Contains(l.UserId))
                .SumAsync(l => (int?)l.Delta, ct) ?? 0);
            if (AiArchitectPricing.CreditsToUsd(free) >= _freeBudgetUsd)
                throw new QuotaExceededException("aiFreePool", AiArchitectPricing.UsdToCredits(_freeBudgetUsd), free, NextMonthStart(),
                    QuotaMessage("aiFreePool", 0, NextMonthStart()));
        }
    }

    private static DateTime MonthStart() => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

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

    private static readonly IReadOnlySet<Guid> NoneLocked = new HashSet<Guid>();

    /// <summary>
    /// Over the limit after a downgrade (launch redesign 4d/4e, decided 14 Oct 2026): the owner keeps as many documents
    /// editable as the plan holds, the ones they chose or else the most recently edited; the others are read-only for
    /// everyone and never deleted. Only after a downgrade: an account that has always been on Free and owns more (made
    /// before limits) is left as it is.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> PlanLockedDocumentsAsync(string ownerId, CancellationToken ct = default)
    {
        if (!_options.Enforce || IsStaff(ownerId)) return NoneLocked;
        var plan = await GetActivePlanAsync(ownerId, ct);
        if (plan is null) return NoneLocked;
        var cap = ReadCap(plan.Caps, "maxDocs");
        if (cap < 0) return NoneLocked;
        var owned = await _context.Documents.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && !d.IsPlayground && !d.IsTemplate && !d.IsHelpContent)
            .OrderByDescending(d => d.UpdatedAt).Select(d => d.Id).ToListAsync(ct);
        if (owned.Count <= cap) return NoneLocked;
        var downgraded = await _context.UserPlans.AnyAsync(
            p => p.UserId == ownerId && p.Status != "active" && p.Plan!.Slug != "free", ct);
        if (!downgraded) return NoneLocked;
        var chosen = await _context.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == ownerId).Select(p => p.EditableDocumentIds).FirstOrDefaultAsync(ct);
        var keep = (chosen ?? []).Where(owned.Contains).Distinct().Take(cap).ToList();
        foreach (var id in owned)
        {
            if (keep.Count >= cap) break;
            if (!keep.Contains(id)) keep.Add(id);
        }
        return owned.Except(keep).ToHashSet();
    }

    /// <summary>How long a choice of editable documents stands before it can be changed (4e: once a day).</summary>
    internal static readonly TimeSpan ChooseEditableEvery = TimeSpan.FromDays(1);

    public async Task<ChooseEditableResult> ChooseEditableAsync(string ownerId, IReadOnlyList<Guid> documentIds, CancellationToken ct = default)
    {
        var locked = await PlanLockedDocumentsAsync(ownerId, ct);
        if (locked.Count == 0) return new ChooseEditableResult("not_over_limit", "All your documents can be edited on your plan.");
        var plan = await GetActivePlanAsync(ownerId, ct);
        var cap = plan is null ? 0 : ReadCap(plan.Caps, "maxDocs");
        var ids = documentIds.Distinct().ToList();
        if (ids.Count != cap)
            return new ChooseEditableResult("wrong_count", $"Choose exactly {cap} documents to keep editing.");
        var owned = await _context.Documents.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && ids.Contains(d.Id)).Select(d => d.Id).CountAsync(ct);
        if (owned != ids.Count) return new ChooseEditableResult("not_yours", "Choose documents you own.");
        var prefs = await _context.UserPreferences.FirstOrDefaultAsync(p => p.UserId == ownerId, ct);
        var now = DateTime.UtcNow;
        if (prefs?.EditableChosenAt is { } at && at + ChooseEditableEvery > now)
            return new ChooseEditableResult("once_a_day", "You can change which documents you edit once a day.", at + ChooseEditableEvery);
        if (prefs is null)
        {
            prefs = new UserPreferences { UserId = ownerId };
            _context.UserPreferences.Add(prefs);
        }
        prefs.EditableDocumentIds = ids;
        prefs.EditableChosenAt = now;
        prefs.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
        return new ChooseEditableResult(null);
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
                Enforced: _aiEnforced,
                ComingSoon: _aiComingSoon && !IsStaff(userId)),
            ReadOnly: await ReadOnlyAsync(userId, plan, ct),
            PdfPages: new PlanMeterDto(await PdfPagesThisMonthAsync(userId, ct), ReadCap(plan.Caps, "pdfPagesPerMonth"), NextMonthStart()));
    }

    private async Task<PlanReadOnlyDto?> ReadOnlyAsync(string userId, ActivePlanDto plan, CancellationToken ct)
    {
        var locked = await PlanLockedDocumentsAsync(userId, ct);
        if (locked.Count == 0) return null;
        var chosenAt = await _context.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId).Select(p => p.EditableChosenAt).FirstOrDefaultAsync(ct);
        var again = chosenAt is { } at && at + ChooseEditableEvery > DateTime.UtcNow ? at + ChooseEditableEvery : (DateTime?)null;
        return new PlanReadOnlyDto(locked.Count, ReadCap(plan.Caps, "maxDocs"), again);
    }

    private static string QuotaMessage(string capKey, int cap, DateTime? resetsAt) => capKey switch
    {
        "maxDocs" => $"Your plan holds {cap} documents. Move one to the Trash or upgrade to start another.",
        "maxImportsPerMonth" => $"Your plan includes {cap} imports a month; the next one is available on {resetsAt:d MMMM}.",
        "aiCreditsPerMonth" => $"This month's Ask Lilia allowance is used; it renews on {resetsAt:d MMMM}.",
        "aiSiteBudget" => $"Lilia's AI is resting until {resetsAt:d MMMM}: this month's AI is used up. Everything else works as usual.",
        "aiFreePool" => $"The free plan's AI is used up for this month; it's back on {resetsAt:d MMMM}. Pro includes Ask Lilia every month.",
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

    public Task<int> RecordAiSpendAsync(string userId, string modelId, int inputTokens, int outputTokens, Guid aiRequestId, CancellationToken ct = default) =>
        RecordAiSpendAsync(userId, modelId, new AiTokenUse(inputTokens, outputTokens), aiRequestId, ct);

    public async Task<int> RecordAiSpendAsync(string userId, string modelId, AiTokenUse use, Guid? aiRequestId, CancellationToken ct = default)
    {
        var credits = _aiCatalog.CreditsFor(modelId, use);
        var (inputTokens, outputTokens) = (use.Input, use.Output);
        _context.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Delta = -credits,
            Reason = "spend",
            AiRequestId = aiRequestId,
            // The cache part only when there is one, so earlier rows and uncached calls read as before.
            Note = use.CacheWrite + use.CacheRead > 0
                ? $"{modelId}: {inputTokens}+{outputTokens} tok (cache write {use.CacheWrite}, read {use.CacheRead})"
                : $"{modelId}: {inputTokens}+{outputTokens} tok",
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

    private readonly Lilia.Api.Directories.ITeamDirectory _teams;

    private Task<int> CountTeamSeatsAsync(string userId, CancellationToken ct) => _teams.SeatsOwnedByAsync(userId, ct);

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
