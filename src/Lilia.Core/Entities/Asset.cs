namespace Lilia.Core.Entities;

public class Asset
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? Url { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UserId { get; set; }
    public string? S3Bucket { get; set; }
    public string? ContentHash { get; set; }
    public int UsageCount { get; set; } = 1;
    public DateTime? LastAccessedAt { get; set; }

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual User? User { get; set; }
}
