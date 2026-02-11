using System.Text.Json;

namespace Lilia.Core.Entities;

public class ConversionAudit
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonDocument? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? DurationMs { get; set; }

    // Navigation properties
    public virtual Job? Job { get; set; }
    public virtual User User { get; set; } = null!;
}
