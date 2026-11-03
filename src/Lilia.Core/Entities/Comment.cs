namespace Lilia.Core.Entities;

public class Comment
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? BlockId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<CommentReply> Replies { get; set; } = new List<CommentReply>();
}
