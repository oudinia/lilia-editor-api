namespace Lilia.Core.Entities;

public class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Group Group { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
}
