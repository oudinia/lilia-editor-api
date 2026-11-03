namespace Lilia.Core.Entities;

public class DocumentGroup
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid GroupId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Role Role { get; set; } = null!;
}
