using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// One turn in an <see cref="AiConversation"/>. The full client-side turn
/// payload (text for user turns; reply/skill/commands/ops/undoVersionId for
/// Lilia turns) is stored verbatim in <see cref="Content"/> so the UI restores
/// losslessly; <see cref="Role"/> is lifted out for querying/ordering.
/// </summary>
public class AiMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }

    /// <summary>"user" or "lilia".</summary>
    public string Role { get; set; } = "user";

    /// <summary>Full turn payload as stored/rendered by the client.</summary>
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");

    /// <summary>AI credits spent producing this turn (lilia turns only).</summary>
    public int CreditsUsed { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual AiConversation Conversation { get; set; } = null!;
}
