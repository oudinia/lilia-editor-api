namespace Lilia.Core.Entities;

/// <summary>
/// A persisted Ask Lilia conversation. Scoped to a document by default
/// (<see cref="DocumentId"/>), but the FK is nullable + reassignable so a
/// conversation can be a general (doc-less) chat, moved, or cloned to another
/// document. The server is the system of record; the client only caches.
/// </summary>
public class AiConversation
{
    public Guid Id { get; set; }

    /// <summary>Owning user (Stytch subject). Primary access key.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Document this conversation belongs to. Null = general chat.
    /// Reassignable to support move/promote.</summary>
    public Guid? DocumentId { get; set; }

    public string Title { get; set; } = "New chat";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }

    // Navigation
    public virtual Document? Document { get; set; }
    public virtual ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
}
