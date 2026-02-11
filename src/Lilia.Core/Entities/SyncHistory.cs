using System.Text.Json;

namespace Lilia.Core.Entities;

public class SyncHistory
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // UPLOAD, PUSH, PULL, SHARE_ENABLE, SHARE_DISABLE, EXPORT
    public int SyncVersion { get; set; }
    public JsonDocument? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
