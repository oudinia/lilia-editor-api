namespace Lilia.Core.Entities;

/// <summary>
/// The immutable half of an import — the source file + user that kicks
/// it off (FT-IMP-001). One row per upload. Instances (today still the
/// ImportReviewSession table; renamed in the PR 4 mirror-realm work)
/// reference this via <see cref="ImportReviewSession.DefinitionId"/>.
///
/// Retry after failure creates a new instance on the same definition —
/// the source file persists here so the rerun doesn't require a
/// re-upload. When the definition is large the RawSource column can
/// hold an S3/R2 pointer instead of the literal bytes (format-specific).
/// </summary>
public class ImportDefinition
{
    public Guid Id { get; set; }

    /// <summary>
    /// Kinde user ID of the uploader. Only the owner can see their own
    /// definitions (and the instances derived from them).
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Display-friendly name for the source. Usually the uploaded file
    /// name; falls back to "Pasted content" for paste-only imports.
    /// </summary>
    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>
    /// tex | latex | docx | markdown | project | pdf — same vocabulary
    /// as <see cref="ImportReviewSession.SourceFormat"/>. Drives the
    /// review-page dispatcher when an instance is opened.
    /// </summary>
    public string SourceFormat { get; set; } = "tex";

    /// <summary>
    /// The raw source (for text formats) or an S3/R2 key (for blobs
    /// large enough to live outside Postgres). Preserved for rerun.
    /// Nullable — format-specific code decides how to populate.
    /// </summary>
    public string? RawSource { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User Owner { get; set; } = null!;
    public virtual ICollection<ImportReviewSession> Instances { get; set; } = new List<ImportReviewSession>();
}
