using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Cached per-block LaTeX validation result keyed by content hash.
///
/// The expensive part of validation is the LaTeX compile (pdflatex shell-out,
/// 100–800ms per block). Once a block's content is validated, the result is
/// valid forever unless the content changes. The (BlockId, ContentHash) pair
/// is unique — identical content never re-validates.
///
/// Insert via bulk-insert helper; never through the EF change tracker.
/// </summary>
public class BlockValidation
{
    public Guid Id { get; set; }

    /// <summary>Block this result belongs to (FK → Blocks).</summary>
    public Guid BlockId { get; set; }

    /// <summary>Denormalised from Block.DocumentId for document-level rollups.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// SHA-256 of the normalised block content (JSON with sorted keys, whitespace
    /// stripped). Identical content across blocks produces identical hashes so
    /// the rollup can count duplicates cheaply.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Closed vocabulary: valid | error | warning. Enforced by CHECK constraint.
    /// "warning" means compile succeeded but diagnostics were emitted.
    /// </summary>
    public string Status { get; set; } = "valid";

    /// <summary>
    /// Error message from the compile (null when Status = valid).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Warnings produced during compile, as a JSON array of strings.
    /// </summary>
    public JsonDocument? Warnings { get; set; }

    /// <summary>
    /// Version identifier of the validation pipeline. Bump when the compile
    /// behaviour meaningfully changes — existing cache entries become stale
    /// and are ignored by GetAsync. Kept short (e.g. "v1", "v2").
    /// </summary>
    public string RuleVersion { get; set; } = "v1";

    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;

    // Navigation (optional — the service doesn't need it, but EF wants a
    // concrete relationship for the cascade-delete to work).
    public virtual Block? Block { get; set; }
    public virtual Document? Document { get; set; }
}
