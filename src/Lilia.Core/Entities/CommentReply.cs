namespace Lilia.Core.Entities;

public class CommentReply
{
    public Guid Id { get; set; }
    public Guid CommentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Comment Comment { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
