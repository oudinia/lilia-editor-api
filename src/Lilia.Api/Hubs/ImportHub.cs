using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Hubs;

/// <summary>
/// SignalR hub for real-time import progress updates.
/// Clients join a group based on jobId to receive progress for their specific import.
/// </summary>
public class ImportHub : Hub
{
    private readonly ILogger<ImportHub> _logger;

    public ImportHub(ILogger<ImportHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client subscribes to progress updates for a specific import job.
    /// </summary>
    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"import-{jobId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to import job {JobId}",
            Context.ConnectionId, jobId);
    }

    /// <summary>
    /// Client unsubscribes from a specific import job.
    /// </summary>
    public async Task UnsubscribeFromJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"import-{jobId}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from import job {JobId}",
            Context.ConnectionId, jobId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
