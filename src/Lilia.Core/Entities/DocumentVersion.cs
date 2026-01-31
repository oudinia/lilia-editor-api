using System.Text.Json;

namespace Lilia.Core.Entities;

public class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string? Name { get; set; }
    public JsonDocument Snapshot { get; set; } = JsonDocument.Parse("{}");
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual User? Creator { get; set; }
}
