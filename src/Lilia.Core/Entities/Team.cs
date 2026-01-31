namespace Lilia.Core.Entities;

public class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Image { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User Owner { get; set; } = null!;
    public virtual ICollection<Group> Groups { get; set; } = new List<Group>();
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
