namespace Lilia.Core.Entities;

public class DocumentCollaborator
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string? InvitedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Role Role { get; set; } = null!;
}
