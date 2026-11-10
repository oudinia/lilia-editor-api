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
    IConfiguration config)
{
    public const string CapKey = "mcpActionsPerDay";
    private static readonly HashSet<string> Free = ["search_help", "get_help"];

    /// <summary>Counts one action; null when it may run, else what to tell the author.</summary>
    public async Task<string?> UseAsync(string userId, string tool, CancellationToken ct)
    {
        if (!options.Value.Enforce || Free.Contains(tool) || options.Value.StaffUserIds.Contains(userId, StringComparer.Ordinal))
            return null;
        var plan = await entitlements.GetActivePlanAsync(userId, ct);
        if (plan is null) return null;
        // A plan without the key (or -1) has no daily limit; Free sets an allowance, Pro a safety cap.
        if (plan.Caps.ValueKind != JsonValueKind.Object || !plan.Caps.TryGetProperty(CapKey, out var v)
            || v.ValueKind != JsonValueKind.Number || v.GetInt32() < 0)
            return null;
        var cap = v.GetInt32();

        var now = DateTime.UtcNow;
        var key = $"mcp-actions:{userId}:{now:yyyy-MM-dd}";
        var used = int.TryParse(await cache.GetStringAsync(key, ct), out var n) ? n : 0;
        if (used >= cap) return Message(plan.Slug, cap, now);
        await cache.SetStringAsync(key, (used + 1).ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpiration = now.Date.AddDays(1).AddHours(1) }, CancellationToken.None);
        return null;
    }

    private string Message(string planSlug, int cap, DateTime now)
    {
        var hours = Math.Max(1, (int)Math.Ceiling((now.Date.AddDays(1) - now).TotalHours));
        var back = $"They're back in about {hours} hour{(hours == 1 ? "" : "s")} (midnight UTC).";
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
        if (userId is not null && allowance is not null && request.Params is { } p
            && await allowance.UseAsync(userId, p.Name, ct) is { } refusal)
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refusal }] };
        return await next(request, ct);
    };
}
