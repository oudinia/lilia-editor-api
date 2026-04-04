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

    // --- Block Locking ---

    /// <summary>
    /// Attempt to lock a block for editing. Returns true if lock was acquired.
    /// Fails if the block is already locked by a different connection.
    /// </summary>
    bool TryLockBlock(string documentId, string blockId, string connectionId);

    /// <summary>
    /// Release a block lock. Only succeeds if the connection owns the lock.
    /// </summary>
    bool UnlockBlock(string documentId, string blockId, string connectionId);

    /// <summary>
    /// Release all block locks held by a connection in a document (used on disconnect/leave).
    /// Returns the list of blockIds that were unlocked.
    /// </summary>
    List<string> UnlockAllBlocks(string documentId, string connectionId);

    /// <summary>
    /// Get all currently locked blocks in a document.
    /// </summary>
    Dictionary<string, (string UserId, string DisplayName)> GetLockedBlocks(string documentId);

    /// <summary>
    /// Check if a block is currently locked.
    /// </summary>
    bool IsBlockLocked(string documentId, string blockId);

    /// <summary>
    /// Get the user who locked a specific block, or null if unlocked.
    /// </summary>
    (string UserId, string DisplayName)? GetBlockLocker(string documentId, string blockId);
}

public class UserPresence
{
    public string ConnectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAt { get; set; }
}
