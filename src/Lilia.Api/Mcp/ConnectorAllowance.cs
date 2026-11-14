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
    OperationPacks packs)
{
    public const string CapKey = "mcpActionsPerDay";
    /// <summary>
    /// Past Free's allowance, with operations bought (packs): reads and edits stay free up to this many actions a day,
    /// Pro's safety cap, so a balance can't become unlimited use.
    /// </summary>
    public const int PackSafetyCap = 500;
    private static readonly HashSet<string> Free = ["search_help", "get_help"];

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
        var key = $"mcp-actions:{userId}:{now:yyyy-MM-dd}";
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

    private Task CountAsync(string key, int used, DateTime now) =>
        cache.SetStringAsync(key, (used + 1).ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpiration = now.Date.AddDays(1).AddHours(1) }, CancellationToken.None);

    private string Message(string planSlug, int cap, DateTime now)
    {
        var hours = Math.Max(1, (int)Math.Ceiling((now.Date.AddDays(1) - now).TotalHours));
        var back = $"They're back in about {hours} hour{(hours == 1 ? "" : "s")} (midnight UTC).";
        if (planSlug == "packs")
            return $"Lilia allows {cap} connector actions a day, a safety limit, and today's are used. {back} " +
                   "Tell the author this; their operations stay on their account, and everything in the Lilia editor itself still works.";
        // Pro's cap is a safety limit (the owner, 10 Nov 2026: 500 a day), not an allowance to upsell.
        if (planSlug != "free")
            return $"Lilia allows {cap} connector actions a day on this plan, a safety limit, and today's are used. {back} " +
                   "If the author needs more, they can write to Lilia's support. Tell the author this; everything in the Lilia editor itself still works.";
        var origin = McpOAuth.PublicOrigin(config);
        return $"Lilia's free plan includes {cap} connector actions a day, and today's are used. {back} " +
               $"Pro allows far more: {origin}/?plans=1. Tell the author this; everything in the Lilia editor itself still works.";
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

    private static bool WithBody(CallToolRequestParams call) =>
        call.Arguments?.TryGetValue("lml", out var lml) == true && lml.ValueKind == System.Text.Json.JsonValueKind.String
        && !string.IsNullOrWhiteSpace(lml.GetString());

    private static Guid? DocumentIdOf(CallToolRequestParams call) =>
        call.Arguments?.TryGetValue("documentId", out var id) == true && id.ValueKind == System.Text.Json.JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var g) ? g : null;
}
