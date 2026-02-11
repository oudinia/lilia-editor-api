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
    public virtual Document Document { get; set; } = null!;
    public virtual Block? Block { get; set; }
    public virtual User User { get; set; } = null!;
    public virtual ICollection<CommentReply> Replies { get; set; } = new List<CommentReply>();
}
