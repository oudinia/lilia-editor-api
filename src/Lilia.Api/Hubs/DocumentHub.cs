using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Hubs;

/// <summary>
/// SignalR hub for real-time document presence.
/// Tracks which users are viewing/editing a document.
/// </summary>
public class DocumentHub : Hub
{
    private readonly ILogger<DocumentHub> _logger;

    // Track connected users per document
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, UserPresence>> _documentUsers = new();

    public DocumentHub(ILogger<DocumentHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client joins a document room to receive presence updates.
    /// </summary>
    public async Task JoinDocument(string documentId, string displayName, string? avatarUrl)
    {
        var groupName = $"doc-{documentId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        var presence = new UserPresence
        {
            ConnectionId = Context.ConnectionId,
            UserId = Context.UserIdentifier ?? Context.ConnectionId,
            DisplayName = displayName,
            AvatarUrl = avatarUrl,
            JoinedAt = DateTime.UtcNow
        };

        var users = _documentUsers.GetOrAdd(documentId, _ => new ConcurrentDictionary<string, UserPresence>());
        users[Context.ConnectionId] = presence;

        // Notify others that someone joined
        await Clients.OthersInGroup(groupName).SendAsync("UserJoined", presence);

        // Send the new client the current user list
        await Clients.Caller.SendAsync("CurrentUsers", users.Values.ToList());

        _logger.LogDebug("User {DisplayName} joined document {DocumentId}",
            displayName, documentId);
    }

    /// <summary>
    /// Client leaves a document room.
    /// </summary>
    public async Task LeaveDocument(string documentId)
    {
        var groupName = $"doc-{documentId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        if (_documentUsers.TryGetValue(documentId, out var users))
        {
            if (users.TryRemove(Context.ConnectionId, out var presence))
            {
                await Clients.Group(groupName).SendAsync("UserLeft", presence.UserId);
            }

            // Clean up empty document entries
            if (users.IsEmpty)
            {
                _documentUsers.TryRemove(documentId, out _);
            }
        }

        _logger.LogDebug("User left document {DocumentId}", documentId);
    }

    /// <summary>
    /// Broadcast cursor position to other users (for future co-editing).
    /// </summary>
    public async Task UpdateCursor(string documentId, int line, int column)
    {
        var groupName = $"doc-{documentId}";
        var userId = Context.UserIdentifier ?? Context.ConnectionId;

        await Clients.OthersInGroup(groupName).SendAsync("CursorMoved", userId, line, column);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Remove user from all document rooms
        foreach (var (documentId, users) in _documentUsers)
        {
            if (users.TryRemove(Context.ConnectionId, out var presence))
            {
                var groupName = $"doc-{documentId}";
                await Clients.Group(groupName).SendAsync("UserLeft", presence.UserId);

                if (users.IsEmpty)
                {
                    _documentUsers.TryRemove(documentId, out _);
                }
            }
        }

        _logger.LogDebug("Client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}

public class UserPresence
{
    public string ConnectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAt { get; set; }
}
