namespace Lilia.Api.Services;

/// <summary>
/// Plan limits ("Entitlements" section). Off until the launch switches them on: while
/// <see cref="Enforce"/> is false, quotas are reported by <c>GET /api/plan</c> but nothing is refused.
/// </summary>
public class EntitlementOptions
{
    /// <summary>Refuse a 4th document or import when the plan's limit is reached.</summary>
    public bool Enforce { get; set; }

    /// <summary>
    /// Accounts that hold the Beta plan (everything of Pro) without a subscription: the owner's
    /// account, which holds the test documents the connector runs use. Stytch user ids.
    /// </summary>
    public string[] StaffUserIds { get; set; } = [];
}
