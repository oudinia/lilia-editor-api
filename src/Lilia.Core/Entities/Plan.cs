using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Subscription plan / tier definition. One row per SKU. Caps + Features
/// are jsonb for flexibility — the entitlement service reads them when
/// enforcing quotas, the frontend reads them for pricing tables.
///
/// Mutating a Plan row changes the offering for everyone on that plan —
/// typically done via seed data in a migration, not at runtime.
/// </summary>
public class Plan
{
    public Guid Id { get; set; }

    /// <summary>Stable identifier for code. CHECK-constrained in the DB.</summary>
    public string Slug { get; set; } = string.Empty;    // free | student | pro | team | epub | compliance_pro | enterprise

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>USD monthly price. Null = free or custom-quoted.</summary>
    public decimal? MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }

    /// <summary>
    /// Resource caps. Shape:
    ///   { "maxDocs": 3, "maxImportsPerMonth": 1, "aiCreditsPerMonth": 0,
    ///     "maxTeamSeats": 0, "maxAssetsPerDoc": 20 }
    /// -1 in any slot means "unlimited".
    /// </summary>
    public JsonDocument Caps { get; set; } = JsonDocument.Parse("{}");

    /// <summary>
    /// Feature keys this plan grants. Used for nav-sidebar filtering and
    /// [RequireFeature] attribute checks.
    /// Example: ["editor","export_latex","export_pdf","ai","team","api_access"].
    /// </summary>
    public JsonDocument Features { get; set; } = JsonDocument.Parse("[]");

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<UserPlan> UserPlans { get; set; } = new List<UserPlan>();
}
