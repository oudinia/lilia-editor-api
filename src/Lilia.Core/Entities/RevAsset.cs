namespace Lilia.Core.Entities;

/// <summary>
/// Mirror of <see cref="Asset"/> inside the import domain (FT-IMP-001).
/// Figures / attachments extracted from an upload get staged here until
/// checkout. The binary payload sits in the same R2/S3 bucket the real
/// assets use (storage_key pass-through); the rev row just holds the
/// metadata + FK to the rev-document.
///
/// Scoped per instance — if a user reruns an import, the previous
/// instance's rev_assets are cascade-deleted along with the superseded
/// instance. Storage cleanup of orphaned blobs is the retention job's
/// responsibility (stage 9).
/// </summary>
public class RevAsset
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owning rev-document. Cascade on doc purge.
    /// </summary>
    public Guid RevDocumentId { get; set; }
    public virtual RevDocument RevDocument { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }

    /// <summary>
    /// Storage key in R2/S3. Mirror of <see cref="Asset.StorageKey"/>.
    /// Pass-through at checkout — the real Asset row reuses the same key,
    /// so the underlying blob never moves.
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;

    public string? Url { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? ContentHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
