namespace Lilia.Core.Entities;

public class Label
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; } // Hex color
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual ICollection<DocumentLabel> DocumentLabels { get; set; } = new List<DocumentLabel>();
}
