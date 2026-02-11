using System.Text.Json;

namespace Lilia.Core.Entities;

public class ImportReviewActivity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BlockId { get; set; }
    public JsonDocument? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ImportReviewSession Session { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
