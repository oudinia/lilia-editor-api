using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Hubs;

/// <summary>
/// SignalR hub for real-time import review collaboration.
/// Clients join a group based on sessionId to receive review updates.
/// </summary>
public class ImportReviewHub : Hub
{
    private readonly ILogger<ImportReviewHub> _logger;

    public ImportReviewHub(ILogger<ImportReviewHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client joins a review session group to receive updates.
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"review-{sessionId}");
        _logger.LogDebug("Client {ConnectionId} joined review session {SessionId}",
            Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Client leaves a review session group.
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"review-{sessionId}");
        _logger.LogDebug("Client {ConnectionId} left review session {SessionId}",
            Context.ConnectionId, sessionId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Import review client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Import review client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
