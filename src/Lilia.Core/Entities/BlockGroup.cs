using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Block-level grouping primitive (LILIA-136). A group is a named bag of
/// blocks scoped to a document and a single "dimension" — currently
/// "layout" (multi-column regions), but designed so other dimensions
/// (review tags, numbering scopes, style presets, source-attribution,
/// counter scopes, etc.) can land later without schema churn.
///
/// Constraint: a block belongs to **at most one group per dimension**
/// (a block can't be in two layout groups simultaneously, but it can be
/// in a layout group AND a review group AND a numbering group). We
/// enforce this at the service layer, not the DB — race window is
/// tolerable for a single-writer editor and a trigger would be heavy.
///
/// `Attributes` is per-dimension JSON. For "layout":
///   { "columns": 1 | 2 | 3 }
/// </summary>
public class BlockGroup
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Dimension { get; set; } = string.Empty;
    public JsonDocument Attributes { get; set; } = JsonDocument.Parse("{}");
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual ICollection<BlockGroupMembership> Memberships { get; set; }
        = new List<BlockGroupMembership>();
}

public static class BlockGroupDimensions
{
    /// <summary>Multi-column regions (and any other layout-shaping dimension we add).</summary>
    public const string Layout = "layout";
}
