using System.Text.Json;

namespace Lilia.Core.Entities;

public class StudioSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public Guid? FocusedBlockId { get; set; }
    public JsonDocument Layout { get; set; } = JsonDocument.Parse("{}");
    public Guid[] CollapsedIds { get; set; } = [];
    public Guid[] PinnedIds { get; set; } = [];
    public string ViewMode { get; set; } = "tree";
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Document Document { get; set; } = null!;
}
