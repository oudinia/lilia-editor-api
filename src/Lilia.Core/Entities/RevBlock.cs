using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Mirror of <see cref="Block"/> inside the import domain (FT-IMP-001).
/// Full block vocabulary — paragraph, heading, equation, figure, table,
/// code, list, blockquote, theorem, abstract, bibliography, toc,
/// pageBreak. Content shape matches the real Block.Content jsonb
/// exactly so checkout can INSERT SELECT verbatim.
///
/// Coexists with the legacy <see cref="ImportBlockReview"/> table during
/// the 2026-04-23 migration. Stage 6 creates rev_blocks; stage 8
/// (idempotent checkout) rewrites finalize to use rev_blocks instead.
/// After cut-over, <see cref="ImportBlockReview"/> becomes a read-only
/// legacy view and is removed in a follow-up pass.
/// </summary>
public class RevBlock
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owning rev-document. Cascade on instance purge via
    /// <see cref="RevDocument.InstanceId"/>'s cascade to instance.
    /// </summary>
    public Guid RevDocumentId { get; set; }
    public virtual RevDocument RevDocument { get; set; } = null!;

    /// <summary>
    /// Block type from <see cref="BlockTypes"/>. Mirror of
    /// <see cref="Block.Type"/>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Full block content (jsonb). Mirror of <see cref="Block.Content"/>.
    /// </summary>
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");

    public int SortOrder { get; set; }
    public Guid? ParentId { get; set; }
    public int Depth { get; set; }
    public string? Path { get; set; }

    /// <summary>
    /// Review status: kept | deleted | edited. Simplified from the old
    /// {pending, approved, rejected, edited} model — see FT-IMP-001
    /// spec §Review page = Studio-lite.
    /// </summary>
    public string Status { get; set; } = "kept";

    /// <summary>
    /// Block metadata — same shape as <see cref="Block.Metadata"/>.
    /// </summary>
    public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}");

    /// <summary>
    /// Parser confidence 0-100 (ImportBlockReview had this; carried over
    /// for analytics / UI hinting).
    /// </summary>
    public int? Confidence { get; set; }

    /// <summary>
    /// Diagnostic warnings attached to this block during parsing
    /// (jsonb array). Distinct from <see cref="ImportDiagnostic"/> rows
    /// — those are per-instance; this is inline-on-block.
    /// </summary>
    public JsonDocument? Warnings { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
