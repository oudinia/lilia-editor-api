namespace Lilia.Core.Entities;

public class ImportReviewCollaborator
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "reviewer"; // owner, reviewer, viewer
    public string? InvitedBy { get; set; }
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }

    // Navigation properties
    public virtual ImportReviewSession Session { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual User? Inviter { get; set; }
}
