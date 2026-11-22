using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Lilia.Api.Mcp;

/// <summary>
/// The connector's account tool, <c>whoami</c> (22 Nov 2026, launch-bot #269): Claude's connector settings are Claude's
/// own page, so Lilia can't put an upgrade button there, and no tool said which account, plan or allowance a connection
/// used: "am I on Free?" had no answer from Claude. The upgrade path for an MCP user has to come from what Lilia sends
/// back, so this tool, and the plan-limit errors (<see cref="PlanLimitMessage"/>), give it.
///
/// <list type="bullet">
/// <item><b>Only the token's own account.</b> No input: no id, no email, so it can never describe someone else.</item>
/// <item><b>The minimum.</b> A name, a masked email hint, the plan, today's connector allowance and the month's
///   limits. Never the full email, an internal user, org or Stripe id, the token or session, or billing details.</item>
/// <item><b>Read, not recomputed.</b> The allowance is <see cref="ConnectorAllowance.TodayAsync"/> (packs from
///   <see cref="OperationPacks"/>), the month is <see cref="IEntitlementService.GetUsageAsync"/>: the same figures the
///   refusals and the plans dialog use. Ask Lilia is an allowance, never a balance: a fraction, no credits.</item>
/// <item><b>Pricing, not checkout.</b> <see cref="PricingUrl"/> is the public plans page, with nothing personal in it.</item>
/// </list>
/// </summary>
[McpServerToolType]
public sealed class LiliaMcpAccountTools(
    IHttpContextAccessor http,
    LiliaDbContext db,
    IEntitlementService entitlements,
    ConnectorAllowance allowance)
{
    /// <summary>Lilia's public plans and prices: informational, never a checkout, and nothing personal in the query.</summary>
    public const string PricingUrl = "https://liliaeditor.com/#pricing";

    private string UserId =>
        http.HttpContext?.User.FindFirst("sub")?.Value
        ?? http.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Not signed in.");

    [McpServerTool(Name = "whoami", Title = "Your account and plan", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The signed-in author's own account, for questions like \"am I on Free?\" or \"how many actions do I have left today?\": their name, a masked email hint, the plan (name and slug), today's connector allowance (actions used and left, the daily cap, when it resets, operation packs left), the month's limits and use (documents, imports, PDF pages, and Ask Lilia as a fraction of its allowance), which tools need a paid plan, and a link to Lilia's plans and prices. No input: it describes only the account this connection signed in with. It never returns the full email, internal ids or billing details. It doesn't use the day's allowance.")]
    public async Task<object> WhoAmI()
    {
        var userId = UserId;
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Name, u.DisplayUsername, u.Email }).FirstOrDefaultAsync();
        var plan = await entitlements.GetActivePlanAsync(userId);
        var usage = await entitlements.GetUsageAsync(userId);
        var today = await allowance.TodayAsync(userId);

        return new
        {
            name = DisplayName(user?.Name) ?? DisplayName(user?.DisplayUsername),
            emailHint = MaskEmail(user?.Email),
            plan = plan is null ? null : new { name = plan.DisplayName, slug = plan.Slug },
            connector = today is null
                ? new
                {
                    dailyCap = (int?)null,
                    usedToday = (int?)null,
                    leftToday = (int?)null,
                    resetsAt = (DateTime?)null,
                    operationPacksLeft = (int?)null,
                    note = "No daily connector limit applies to this account.",
                }
                : new
                {
                    dailyCap = (int?)today.Cap,
                    usedToday = (int?)today.Used,
                    leftToday = (int?)today.Left,
                    resetsAt = (DateTime?)today.ResetsAt,
                    operationPacksLeft = today.Packs,
                    note = today.SafetyLimit
                        ? $"{today.Cap} connector actions a day is this plan's safety limit; it renews at midnight UTC."
                        : $"This plan includes {today.Cap} connector actions a day; they renew at midnight UTC."
                          + " Past them, operations bought in packs pay for checks, exports and imports, and reads and edits keep working while there is a balance."
                          + " search_help, get_help and whoami don't count.",
                },
            month = usage is null ? null : new
            {
                documents = Meter(usage.Documents, resets: false),
                imports = Meter(usage.Imports),
                pdfPages = usage.PdfPages is null ? null : Meter(usage.PdfPages),
                // An allowance, never a balance (the plans dialog's rule): how far through it, no credits.
                askLilia = new
                {
                    fractionUsed = usage.Ask.Unlimited ? (double?)null : Math.Round(usage.Ask.Fraction, 2),
                    unlimited = usage.Ask.Unlimited,
                    comingSoon = usage.Ask.ComingSoon,
                    resetsAt = usage.Ask.ResetsAt,
                },
            },
            paidPlanTools = await PaidOnlyToolsAsync(),
            pricing = PricingUrl,
        };
    }

    private static object Meter(PlanMeterDto m, bool resets = true) => new
    {
        used = m.Used,
        limit = m.Limit < 0 ? (int?)null : m.Limit,
        unlimited = m.Limit < 0,
        resetsAt = resets ? m.ResetsAt : null,
    };

    /// <summary>
    /// The connector tools Free can't use at all, from the Free plan's own caps (not a list kept here): today only PDF and
    /// scan import, when Free's <c>pdfPagesPerMonth</c> is 0 (it has been 10 since 20 Nov 2026, so none). Everything else
    /// works on every plan, within its limits.
    /// </summary>
    private async Task<object> PaidOnlyToolsAsync()
    {
        var free = await db.Plans.AsNoTracking().Where(p => p.Slug == "free" && p.IsActive).Select(p => p.Caps).FirstOrDefaultAsync();
        var tools = new List<object>();
        if (free is not null && Cap(free.RootElement, "pdfPagesPerMonth") == 0)
            tools.Add(new { tool = "import_document", what = "PDF and scanned-image import (Free includes 0 PDF pages a month)" });
        return new
        {
            tools,
            note = tools.Count == 0
                ? "No connector tool needs a paid plan: every one works on Free, within its limits. Pro raises the limits."
                : "These need a paid plan; every other tool works on Free, within its limits.",
        };
    }

    private static int Cap(JsonElement caps, string key) =>
        caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    /// <summary>A name to greet the author by; never an email (some sign-ups store the address as the name).</summary>
    internal static string? DisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Contains('@') ? null : name.Trim();

    /// <summary>The email's first letter and its domain, <c>o•••@gmail.com</c>: enough to recognise the account.</summary>
    internal static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.Trim().LastIndexOf('@');
        if (at <= 0 || at == email.Trim().Length - 1) return null;
        var e = email.Trim();
        return $"{e[0]}•••{e[at..]}";
    }

    /// <summary>
    /// A plan limit, as the connector says it (22 Nov 2026, launch-bot #269): the limit's own words (which name it), the
    /// date it renews when it has one and the words don't already say it, then <see cref="PricingUrl"/>. Claude relays this
    /// text in chat, the only place an MCP user can be shown the way to upgrade. No account details.
    /// </summary>
    internal static string PlanLimitMessage(QuotaExceededException limit)
    {
        var text = limit.Message.Trim();
        if (limit.ResetsAt is { } at && !text.Contains(at.ToString("d MMMM", CultureInfo.CurrentCulture), StringComparison.Ordinal)
            && !text.Contains(at.ToString("d MMMM", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            text += $" This limit renews on {at.ToString("d MMMM", CultureInfo.InvariantCulture)} (UTC).";
        // A text that already gives its own way to upgrade (the PDF credits one links the plans dialog) gets no second link.
        return text.Contains("http", StringComparison.OrdinalIgnoreCase) ? text : $"{text} {PricingSentence}";
    }

    /// <summary>The sentence every plan-limit text ends with: the link last, so nothing sticks to it.</summary>
    internal const string PricingSentence = "Plans and prices: " + PricingUrl;
}
