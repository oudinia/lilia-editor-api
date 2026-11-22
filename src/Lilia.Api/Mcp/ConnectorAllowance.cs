using System.Globalization;
using System.Text.Json;
using Lilia.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lilia.Api.Mcp;

/// <summary>
/// The connector's daily allowance (the owner, 5 Nov 2026: "Free 50/day, Pro unlimited"; 25 a day since the
/// connector-first launch decision of 10 Nov). An assistant
/// using Lilia costs no AI (the person's own Claude or ChatGPT thinks), but every call runs here, and a
/// check or an export compiles. So Free gets enough actions a day to try Lilia from an assistant, and
/// Pro has no daily limit. The number is the plan's <c>mcpActionsPerDay</c> cap (-1: no limit), so the
/// plans dialog shows the same figure.
///
/// <para>Counted per account and UTC day in the distributed cache (Redis in production, so a restart
/// keeps the count). The help tools don't count: they're cheap, and they're where an author over the
/// limit is sent. Applied with the other plan limits (<c>Entitlements:Enforce</c>); staff are exempt.
/// A refused call is a tool error the assistant reads out, not an HTTP error that looks like a broken
/// connector.</para>
/// </summary>
public sealed class ConnectorAllowance(
    IEntitlementService entitlements,
    IDistributedCache cache,
    IOptions<EntitlementOptions> options,
    IConfiguration config,
    OperationPacks packs,
    IOptions<StripeOptions> stripe)
{
    public const string CapKey = "mcpActionsPerDay";
    /// <summary>
    /// Past Free's allowance, with operations bought (packs): reads and edits stay free up to this many actions a day,
    /// Pro's safety cap, so a balance can't become unlimited use.
    /// </summary>
    public const int PackSafetyCap = 500;
    // whoami too (22 Nov 2026, launch-bot #269): it is where an author at the limit learns when it renews.
    private static readonly HashSet<string> Free = ["search_help", "get_help", "whoami"];

    /// <summary>Whether a call may run: a refusal to tell the author, and the pack operation it spent, if any.</summary>
    public sealed record Decision(string? Refusal, Guid? Spend)
    {
        public static readonly Decision Run = new(null, null);
    }

    /// <summary>Counts one action; null when it may run, else what to tell the author.</summary>
    public async Task<string?> UseAsync(string userId, string tool, CancellationToken ct) =>
        (await DecideAsync(userId, tool, ct)).Refusal;

    /// <summary>
    /// Counts one action. Within the plan's daily allowance it runs. Past Free's, operations bought in packs take over
    /// (the owner, 14 Nov 2026): a check, an export or an import spends one; reads and edits run free while there is a
    /// balance, up to <see cref="PackSafetyCap"/> actions a day. Pro's cap is a safety limit and packs don't lift it.
    /// </summary>
    public async Task<Decision> DecideAsync(string userId, string tool, CancellationToken ct)
    {
        if (!options.Value.Enforce || Free.Contains(tool) || options.Value.StaffUserIds.Contains(userId, StringComparer.Ordinal))
            return Decision.Run;
        var plan = await entitlements.GetActivePlanAsync(userId, ct);
        if (plan is null) return Decision.Run;
        // A plan without the key (or -1) has no daily limit; Free sets an allowance, Pro a safety cap.
        if (plan.Caps.ValueKind != JsonValueKind.Object || !plan.Caps.TryGetProperty(CapKey, out var v)
            || v.ValueKind != JsonValueKind.Number || v.GetInt32() < 0)
            return Decision.Run;
        var cap = v.GetInt32();

        var now = DateTime.UtcNow;
        var key = DayKey(userId, now);
        var used = int.TryParse(await cache.GetStringAsync(key, ct), out var n) ? n : 0;
        if (used < cap)
        {
            await CountAsync(key, used, now);
            return Decision.Run;
        }
        if (plan.Slug != "free") return new Decision(Message(plan.Slug, cap, now), null);

        // Free, past the allowance: operations bought in packs.
        if (OperationPacks.PaidTools.Contains(tool))
        {
            var spend = await packs.TrySpendAsync(userId, tool, ct);
            if (spend is null) return new Decision(Message(plan.Slug, cap, now), null);
            await CountAsync(key, used, now);
            return new Decision(null, spend);
        }
        if (used < PackSafetyCap && await packs.BalanceAsync(userId, ct) > 0)
        {
            await CountAsync(key, used, now);
            return Decision.Run;
        }
        return new Decision(used >= PackSafetyCap ? Message("packs", PackSafetyCap, now) : Message(plan.Slug, cap, now), null);
    }

    private Task CountAsync(string key, int used, DateTime now, int by = 1) =>
        cache.SetStringAsync(key, (used + by).ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpiration = now.Date.AddDays(1).AddHours(1) }, CancellationToken.None);

    private static string DayKey(string userId, DateTime now) => $"mcp-actions:{userId}:{now:yyyy-MM-dd}";

    // ── PDF and scan import: credits per page (the owner, 20 Nov 2026) ────────────────────────────────────────────────

    /// <summary>
    /// What one page read by Mathpix costs, in the connector's credits (operations): <c>Mathpix:CreditsPerPage</c>. A scanned
    /// image is one page. 0 by default since 23 Nov 2026 (the owner, on Grok's bulk retest: 16 pages took 160 of Pro's 500
    /// daily actions, so a notebook of 150 pages couldn't pass in a day): a PDF page counts once, against the plan's monthly
    /// PDF pages, not again as daily actions. Set it above 0 to charge pages as actions again.
    /// </summary>
    public int CreditsPerPage => Math.Max(0, config.GetValue("Mathpix:CreditsPerPage", 0));

    /// <summary>The credits used today from the daily allowance (every connector action, and the pages imported).</summary>
    public async Task<int> UsedTodayAsync(string userId, CancellationToken ct = default) =>
        int.TryParse(await cache.GetStringAsync(DayKey(userId, DateTime.UtcNow), ct), out var n) ? n : 0;

    /// <summary>
    /// Where the author's credits stand for a PDF or scan import: null when nothing limits them (the plan limits off,
    /// staff, a plan with no daily allowance); else the plan, the credits left today and, on Free, the packs' balance.
    /// The same rules as <see cref="DecideAsync"/>: the daily allowance first, then packs on Free; Pro's 500 a day is a
    /// safety cap that packs don't lift.
    /// </summary>
    private async Task<(string Slug, int Cap, int Used, int LeftToday, int Packs)?> StandingAsync(string userId, CancellationToken ct)
    {
        if (!options.Value.Enforce || options.Value.StaffUserIds.Contains(userId, StringComparer.Ordinal)) return null;
        var plan = await entitlements.GetActivePlanAsync(userId, ct);
        if (plan is null || plan.Caps.ValueKind != JsonValueKind.Object || !plan.Caps.TryGetProperty(CapKey, out var v)
            || v.ValueKind != JsonValueKind.Number || v.GetInt32() < 0)
            return null;
        var cap = v.GetInt32();
        var used = await UsedTodayAsync(userId, ct);
        var packBalance = plan.Slug == "free" ? Math.Max(0, await packs.BalanceAsync(userId, ct)) : 0;
        return (plan.Slug, cap, used, Math.Max(0, cap - used), packBalance);
    }

    /// <summary>Today's connector allowance, for <c>whoami</c>: the cap, the actions used and left, when it renews and, on Free, the packs' balance.</summary>
    /// <param name="SafetyLimit">The cap is a safety limit (Pro's 500 a day), not an allowance; packs don't lift it.</param>
    public sealed record Today(int Cap, int Used, int Left, DateTime ResetsAt, int? Packs, bool SafetyLimit);

    /// <summary>
    /// Where the author stands today (22 Nov 2026, launch-bot #269: <c>whoami</c>), from the same reading the refusals use;
    /// null when no daily limit applies (the plan limits off, staff, a plan with no daily cap).
    /// </summary>
    public async Task<Today?> TodayAsync(string userId, CancellationToken ct = default) =>
        await StandingAsync(userId, ct) is { } s
            ? new Today(s.Cap, s.Used, s.LeftToday, DateTime.UtcNow.Date.AddDays(1), s.Slug == "free" ? s.Packs : null, s.Slug != "free")
            : null;

    /// <summary>
    /// Refuses, before anything reaches Mathpix, a PDF or scan whose pages cost more credits than the author has: what is
    /// left of today's allowance, plus their packs on Free. Throws a <see cref="QuotaExceededException"/> ("pdfCredits")
    /// that says the pages, the credits and what is left. A refused import costs nothing.
    /// </summary>
    public async Task EnsureCreditsForPagesAsync(string userId, int pages, bool image, CancellationToken ct = default)
    {
        var credits = Math.Max(1, pages) * CreditsPerPage;
        if (credits == 0 || await StandingAsync(userId, ct) is not { } s) return;
        var available = s.LeftToday + s.Packs;
        if (credits <= available) return;
        var now = DateTime.UtcNow;
        var what = image ? "This scanned page" : $"This {pages}-page PDF";
        var have = s.Packs > 0
            ? $"you have {available} ({s.LeftToday} left today and {s.Packs} from your packs)"
            : $"you have {available} left today";
        var next = s.Slug == "free"
            ? $"Try a shorter PDF, buy a pack ({McpOAuth.PublicOrigin(config)}/settings/billing), or upgrade ({McpOAuth.PublicOrigin(config)}/?plans=1)."
            : "Try a shorter PDF, or import it after midnight UTC, when the day's credits renew.";
        throw new QuotaExceededException("pdfCredits", s.Cap, s.Used, now.Date.AddDays(1),
            $"{what} uses {credits} credits ({CreditsPerPage} a page); {have}. {next}");
    }

    /// <summary>
    /// Charges the pages Mathpix read: <see cref="CreditsPerPage"/> each, from today's allowance first, then (on Free) from
    /// packs, as tool calls are counted. Never refuses: Mathpix has already billed them, so a page read beyond what was
    /// left (Mathpix counted more pages than the file showed) still goes on the day's count.
    /// </summary>
    /// <param name="capToAvailable">
    /// For a PDF whose pages couldn't be counted before Mathpix (the review of API #670, 20 Nov 2026): the charge stops at
    /// the credits the author has (today's left, plus packs on Free), never below zero, since the pre-check could only
    /// ask for one page's worth.
    /// </param>
    public async Task ChargePagesAsync(string userId, int pages, CancellationToken ct = default, bool capToAvailable = false)
    {
        var credits = Math.Max(0, pages) * CreditsPerPage;
        if (credits == 0 || await StandingAsync(userId, ct) is not { } s) return;
        if (capToAvailable) credits = Math.Min(credits, s.LeftToday + s.Packs);
        if (credits == 0) return;
        var now = DateTime.UtcNow;
        var fromPacks = Math.Min(s.Packs, Math.Max(0, credits - s.LeftToday));
        if (fromPacks > 0)
            fromPacks = await packs.SpendAsync(userId, "import.pdf", fromPacks, ct);
        await CountAsync(DayKey(userId, now), s.Used, now, credits - fromPacks);
    }

    /// <summary>
    /// What a refused call tells the author. Since 22 Nov 2026 (launch-bot #269) each names the limit and the day it
    /// renews; Free's ends with Lilia's plans and prices (<see cref="LiliaMcpAccountTools.PricingUrl"/>): Claude relays it
    /// in chat, the only place an MCP user can see the way to upgrade. Pro's safety limits don't upsell.
    /// </summary>
    private string Message(string planSlug, int cap, DateTime now)
    {
        var hours = Math.Max(1, (int)Math.Ceiling((now.Date.AddDays(1) - now).TotalHours));
        var renews = now.Date.AddDays(1).ToString("d MMMM", CultureInfo.InvariantCulture);
        var back = $"They're back in about {hours} hour{(hours == 1 ? "" : "s")} (midnight UTC, {renews}).";
        if (planSlug == "packs")
            return $"Lilia allows {cap} connector actions a day, a safety limit, and today's are used. {back} " +
                   "Tell the author this; their operations stay on their account, and everything in the Lilia editor itself still works.";
        // Pro's cap is a safety limit (the owner, 10 Nov 2026: 500 a day), not an allowance to upsell: no pricing link here.
        if (planSlug != "free")
            return $"Lilia allows {cap} connector actions a day on this plan, a safety limit, and today's are used. {back} " +
                   "If the author needs more, they can write to Lilia's support. Tell the author this; everything in the Lilia editor itself still works.";
        var origin = McpOAuth.PublicOrigin(config);
        // Packs, once they're on sale (the owner, 14 Nov 2026: "shown … in the connector limit message").
        // In euros, as Stripe charges them and the website sells them (launch-bot #215, 19 Nov 2026: it said "$5").
        var packOffer = stripe.Value.IsConfigured && !string.IsNullOrEmpty(stripe.Value.OperationsPackPriceId)
            ? $" Or the author can buy operations for checks, exports and imports: €{(stripe.Value.OperationsPackUnitCents / 100m).ToString("0.##", CultureInfo.InvariantCulture)} for {OperationPacks.PerPack}, never expiring, in {origin}/settings/billing."
            : "";
        return $"Lilia's free plan includes {cap} connector actions a day, and today's are used. {back}{packOffer} " +
               $"Tell the author this; everything in the Lilia editor itself still works. Pro allows far more. {LiliaMcpAccountTools.PricingSentence}";
    }

    /// <summary>The call-tool filter that applies it to every connector call.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (request, ct) =>
    {
        var user = request.User;
        var userId = user?.FindFirst("sub")?.Value ?? user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var allowance = request.Services?.GetService<ConnectorAllowance>();
        var decision = userId is not null && allowance is not null && request.Params is { } p
            ? await allowance.DecideAsync(userId, p.Name, ct)
            : Decision.Run;
        if (decision.Refusal is { } refusal)
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refusal }] };
        CallToolResult result;
        try
        {
            result = await next(request, ct);
        }
        catch
        {
            if (decision.Spend is { } thrown) await RefundAsync(request.Services, thrown);
            throw;
        }
        // A pack operation spent on a call that failed is given back.
        if (decision.Spend is { } spent && Failed(result)) await RefundAsync(request.Services, spent);
        // Every connector action is an operation, weighted by its kind (11 Nov 2026); a refused or failed one isn't.
        if (userId is not null && !Failed(result) && request.Params is { } call
            && OperationLog.ConnectorCode(call.Name, WithBody(call)) is { } code
            && request.Services?.GetService<OperationLog>() is { } operations)
            await operations.RecordAsync(userId, DocumentIdOf(call), code, "connector");
        return result;
    };

    private static async Task RefundAsync(IServiceProvider? services, Guid spend)
    {
        if (services?.GetService<OperationPacks>() is { } packs) await packs.RefundAsync(spend);
    }

    /// <summary>A tool error, or a reply whose JSON says "error" (a document the author can't open): not counted.</summary>
    internal static bool Failed(CallToolResult result)
    {
        if (result.IsError == true) return true;
        if (result.Content is not { Count: > 0 } || result.Content[0] is not TextContentBlock { Text: { } text }) return false;
        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out _);
        }
        catch (JsonException) { return false; }
    }

    // create_document with a first draft: in LaTeX (content) or, advanced, LML (lml).
    private static bool WithBody(CallToolRequestParams call) => HasText(call, "content") || HasText(call, "lml");

    private static bool HasText(CallToolRequestParams call, string name) =>
        call.Arguments?.TryGetValue(name, out var v) == true && v.ValueKind == System.Text.Json.JsonValueKind.String
        && !string.IsNullOrWhiteSpace(v.GetString());

    private static Guid? DocumentIdOf(CallToolRequestParams call) =>
        call.Arguments?.TryGetValue("documentId", out var id) == true && id.ValueKind == System.Text.Json.JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var g) ? g : null;
}
