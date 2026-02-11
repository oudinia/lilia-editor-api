namespace Lilia.Api.Services;

public interface IPresenceService
{
    void AddUser(string documentId, string connectionId, UserPresence presence);
    bool RemoveUser(string documentId, string connectionId, out UserPresence? presence);
    List<UserPresence> GetUsers(string documentId);
    bool IsDocumentEmpty(string documentId);
    void CleanupDocument(string documentId);

    /// <summary>
    /// Remove a connection from all documents (used on disconnect).
    /// Returns the list of (documentId, presence) pairs that were removed.
    /// </summary>
    List<(string DocumentId, UserPresence Presence)> RemoveConnection(string connectionId);
}

public class UserPresence
{
    public string ConnectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAt { get; set; }
}
