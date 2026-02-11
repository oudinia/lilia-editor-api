using System.Text.Json;

namespace Lilia.Core.Entities;

public class AiChat
{
    public string Id { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string? UserId { get; set; }
    public string? Title { get; set; }
    public JsonDocument? Messages { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Organization? Organization { get; set; }
    public virtual User? User { get; set; }
}
