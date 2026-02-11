using System.Text.Json;

namespace Lilia.Core.Entities;

public class DocumentSnapshot
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? CreatedBy { get; set; }
    public string? Name { get; set; }
    public JsonDocument BlocksSnapshot { get; set; } = JsonDocument.Parse("[]");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual User? Creator { get; set; }
}
