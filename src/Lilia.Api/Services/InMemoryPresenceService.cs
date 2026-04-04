using System.Collections.Concurrent;

namespace Lilia.Api.Services;

/// <summary>
/// In-memory presence tracking. Works for a single server.
/// Swap to a Redis-backed implementation when scaling horizontally.
/// </summary>
public class InMemoryPresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, UserPresence>> _documentUsers = new();

    // Block locking: documentId → (blockId → locker info)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (string ConnectionId, string UserId, string DisplayName)>> _blockLocks = new();

    public void AddUser(string documentId, string connectionId, UserPresence presence)
    {
        var users = _documentUsers.GetOrAdd(documentId, _ => new ConcurrentDictionary<string, UserPresence>());
        users[connectionId] = presence;
    }

    public bool RemoveUser(string documentId, string connectionId, out UserPresence? presence)
    {
        presence = null;
        if (!_documentUsers.TryGetValue(documentId, out var users))
            return false;

        return users.TryRemove(connectionId, out presence);
    }

    public List<UserPresence> GetUsers(string documentId)
    {
        if (_documentUsers.TryGetValue(documentId, out var users))
            return users.Values.ToList();

        return [];
    }

    public bool IsDocumentEmpty(string documentId)
    {
        if (!_documentUsers.TryGetValue(documentId, out var users))
            return true;

        return users.IsEmpty;
    }

    public void CleanupDocument(string documentId)
    {
        if (_documentUsers.TryGetValue(documentId, out var users) && users.IsEmpty)
        {
            _documentUsers.TryRemove(documentId, out _);
        }
    }

    public List<(string DocumentId, UserPresence Presence)> RemoveConnection(string connectionId)
    {
        var removed = new List<(string, UserPresence)>();

        foreach (var (documentId, users) in _documentUsers)
        {
            if (users.TryRemove(connectionId, out var presence))
            {
                removed.Add((documentId, presence));

                if (users.IsEmpty)
                {
                    _documentUsers.TryRemove(documentId, out _);
                }
            }
        }

        return removed;
    }

    // --- Block Locking ---

    public bool TryLockBlock(string documentId, string blockId, string connectionId)
    {
        var locks = _blockLocks.GetOrAdd(documentId, _ => new ConcurrentDictionary<string, (string, string, string)>());

        // Look up the user info from presence
        var users = _documentUsers.GetOrAdd(documentId, _ => new ConcurrentDictionary<string, UserPresence>());
        users.TryGetValue(connectionId, out var presence);
        var userId = presence?.UserId ?? connectionId;
        var displayName = presence?.DisplayName ?? "Unknown";

        // Try to add the lock — fails if key already exists
        if (locks.TryAdd(blockId, (connectionId, userId, displayName)))
            return true;

        // Already locked — check if it's the same connection (re-lock is OK)
        if (locks.TryGetValue(blockId, out var existing) && existing.ConnectionId == connectionId)
            return true;

        return false;
    }

    public bool UnlockBlock(string documentId, string blockId, string connectionId)
    {
        if (!_blockLocks.TryGetValue(documentId, out var locks))
            return false;

        if (!locks.TryGetValue(blockId, out var existing))
            return false;

        if (existing.ConnectionId != connectionId)
            return false;

        return locks.TryRemove(blockId, out _);
    }

    public List<string> UnlockAllBlocks(string documentId, string connectionId)
    {
        var unlocked = new List<string>();

        if (!_blockLocks.TryGetValue(documentId, out var locks))
            return unlocked;

        foreach (var (blockId, locker) in locks)
        {
            if (locker.ConnectionId == connectionId)
            {
                if (locks.TryRemove(blockId, out _))
                    unlocked.Add(blockId);
            }
        }

        // Clean up empty dictionary
        if (locks.IsEmpty)
            _blockLocks.TryRemove(documentId, out _);

        return unlocked;
    }

    public Dictionary<string, (string UserId, string DisplayName)> GetLockedBlocks(string documentId)
    {
        if (!_blockLocks.TryGetValue(documentId, out var locks))
            return new Dictionary<string, (string, string)>();

        return locks.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.UserId, kvp.Value.DisplayName)
        );
    }

    public bool IsBlockLocked(string documentId, string blockId)
    {
        return _blockLocks.TryGetValue(documentId, out var locks) && locks.ContainsKey(blockId);
    }

    public (string UserId, string DisplayName)? GetBlockLocker(string documentId, string blockId)
    {
        if (!_blockLocks.TryGetValue(documentId, out var locks))
            return null;

        if (!locks.TryGetValue(blockId, out var locker))
            return null;

        return (locker.UserId, locker.DisplayName);
    }
}
