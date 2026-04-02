using System.Text.Json;

namespace Lilia.Core.Entities;

public class Feedback
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Type { get; set; } = "general"; // bug, feature, ux, question, general
    public string Message { get; set; } = "";
    public string? Page { get; set; } // route/URL where feedback was submitted
    public string? BlockType { get; set; } // if feedback is about a specific block type
    public string? BlockId { get; set; } // if feedback is about a specific block
    public string? DocumentId { get; set; } // document context
    public string Status { get; set; } = "new"; // new, acknowledged, in-progress, resolved, dismissed
    public JsonDocument? Metadata { get; set; } // browser, device, viewport, etc.
    public string? Response { get; set; } // admin response
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
}
