using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Mirror of <see cref="Document"/> inside the import domain
/// (FT-IMP-001). One per <see cref="ImportReviewSession"/> (instance) —
/// holds the title, metadata, and whatever else a real editor document
/// would carry, pre-checkout. At checkout the row is INSERT SELECT'd
/// verbatim into the <c>documents</c> table; Id passes through unchanged.
///
/// Why separate from <c>documents</c>: editor code reads from
/// <c>documents</c> and never touches <c>rev_*</c>. Import code writes
/// only to <c>rev_*</c>. Checkout is the single crossing. See
/// <c>lilia-docs/specs/import-staging-cart.md §Data model</c>.
/// </summary>
public class RevDocument
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owning instance. Cascade-delete on instance purge (retention job).
    /// </summary>
    public Guid InstanceId { get; set; }
    public virtual ImportReviewSession Instance { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Free-form metadata — equivalent to <see cref="Document.Metadata"/>.
    /// Preserved verbatim at checkout.
    /// </summary>
    public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}");

    /// <summary>
    /// Source format this doc was imported from (tex / docx / markdown / ...).
    /// Mostly tracked for analytics; checkout copies it through.
    /// </summary>
    public string? SourceFormat { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<RevBlock> Blocks { get; set; } = new List<RevBlock>();
}
