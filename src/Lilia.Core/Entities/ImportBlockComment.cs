namespace Lilia.Core.Entities;

public class ImportBlockComment
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ImportReviewSession Session { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual User? Resolver { get; set; }
}
