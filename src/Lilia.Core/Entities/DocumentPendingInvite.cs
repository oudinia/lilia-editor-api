namespace Lilia.Core.Entities;

public class DocumentPendingInvite
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string InvitedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending, accepted, expired
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    // Navigation properties
}
