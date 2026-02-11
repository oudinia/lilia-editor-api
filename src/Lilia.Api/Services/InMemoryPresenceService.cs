using System.Collections.Concurrent;

namespace Lilia.Api.Services;

/// <summary>
/// In-memory presence tracking. Works for a single server.
/// Swap to a Redis-backed implementation when scaling horizontally.
/// </summary>
public class InMemoryPresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, UserPresence>> _documentUsers = new();

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
}
