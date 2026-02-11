using Lilia.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Hubs;

/// <summary>
/// SignalR hub for real-time document presence.
/// Tracks which users are viewing/editing a document.
/// </summary>
public class DocumentHub : Hub
{
    private readonly IPresenceService _presence;
    private readonly ILogger<DocumentHub> _logger;

    public DocumentHub(IPresenceService presence, ILogger<DocumentHub> logger)
    {
        _presence = presence;
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

        _presence.AddUser(documentId, Context.ConnectionId, presence);

        // Notify others that someone joined
        await Clients.OthersInGroup(groupName).SendAsync("UserJoined", presence);

        // Send the new client the current user list
        var users = _presence.GetUsers(documentId);
        await Clients.Caller.SendAsync("CurrentUsers", users);

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

        if (_presence.RemoveUser(documentId, Context.ConnectionId, out var presence))
        {
            await Clients.Group(groupName).SendAsync("UserLeft", presence!.UserId);
        }

        _presence.CleanupDocument(documentId);

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
        var removed = _presence.RemoveConnection(Context.ConnectionId);

        foreach (var (documentId, presence) in removed)
        {
            var groupName = $"doc-{documentId}";
            await Clients.Group(groupName).SendAsync("UserLeft", presence.UserId);
        }

        _logger.LogDebug("Client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
