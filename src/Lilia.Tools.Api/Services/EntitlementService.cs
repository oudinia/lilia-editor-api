using Microsoft.Extensions.Configuration;

namespace Lilia.Tools.Api.Services;

/// <summary>What a caller has paid for. Ordered: each tier contains the one below.</summary>
public enum ToolTier
{
    /// <summary>Anonymous or signed-in without a purchase.</summary>
    Free = 0,

    /// <summary>One document: unlimited tables in it, one shared house style.</summary>
    DocumentPass = 1,

    /// <summary>Every tool, and styles that persist across documents.</summary>
    LiliaPass = 2,
}

/// <summary>
/// What the caller is allowed to do. Resolved server-side and enforced server-side —
/// the client renders a paywall from this, but never decides it.
/// </summary>
/// <param name="Tier">The tier the caller holds.</param>
/// <param name="CanUseExportPresets">Export presets are the paid unlock; the LaTeX source is always free.</param>
/// <param name="CanSaveStyles">Styles that outlive a single document.</param>
public record Entitlement(ToolTier Tier, bool CanUseExportPresets, bool CanSaveStyles)
{
    public static Entitlement For(ToolTier tier) => new(
        Tier: tier,
        CanUseExportPresets: tier >= ToolTier.DocumentPass,
        CanSaveStyles: tier >= ToolTier.LiliaPass);
}

public interface IEntitlementService
{
    /// <summary>Resolve what this caller has. Never throws; unknown callers are Free.</summary>
    Entitlement Resolve(string? userId);
}

/// <summary>
/// Entitlement resolution with **no payment provider attached**.
///
/// <para>This is the seam a provider plugs into later, and it exists now so that
/// gating is enforced on the server from the start rather than retrofitted. Every
/// paid capability is already checked here; what is missing is only the thing that
/// *grants* a tier.</para>
///
/// <para>Until then, tiers are granted from configuration —
/// <c>Tools:Entitlements:DocumentPass</c> and <c>Tools:Entitlements:LiliaPass</c>,
/// each a list of user ids. That is deliberately manual: it lets a real customer be
/// served the day they pay, by hand, without pretending a billing integration
/// exists. Anonymous callers are always Free, because there is nothing to attach a
/// purchase to.</para>
/// </summary>
public class EntitlementService : IEntitlementService
{
    private readonly HashSet<string> _documentPass;
    private readonly HashSet<string> _liliaPass;

    public EntitlementService(IConfiguration configuration)
    {
        _documentPass = Read(configuration, "Tools:Entitlements:DocumentPass");
        _liliaPass = Read(configuration, "Tools:Entitlements:LiliaPass");
    }

    private static HashSet<string> Read(IConfiguration configuration, string key) =>
        configuration.GetSection(key).Get<string[]>() is { } ids
            ? new HashSet<string>(ids, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    public Entitlement Resolve(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return Entitlement.For(ToolTier.Free);
        if (_liliaPass.Contains(userId)) return Entitlement.For(ToolTier.LiliaPass);
        if (_documentPass.Contains(userId)) return Entitlement.For(ToolTier.DocumentPass);
        return Entitlement.For(ToolTier.Free);
    }
}
