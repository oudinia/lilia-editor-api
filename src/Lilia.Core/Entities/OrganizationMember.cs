namespace Lilia.Core.Entities;

public class OrganizationMember
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "member";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Organization Organization { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
