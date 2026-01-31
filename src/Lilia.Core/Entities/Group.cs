namespace Lilia.Core.Entities;

public class Group
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Team Team { get; set; } = null!;
    public virtual ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public virtual ICollection<DocumentGroup> DocumentGroups { get; set; } = new List<DocumentGroup>();
}
