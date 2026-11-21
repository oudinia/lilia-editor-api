using System.Collections.Concurrent;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>Tells the author's open pages how a file stands.</summary>
public interface IBulkImportNotifier
{
    /// <summary>
    /// Sends <c>BulkImportProgress</c> to the author's group. Throttled per file to about 2 a second, unless
    /// <paramref name="force"/> (a chunk placed or failed, a file finished): those are always sent.
    /// </summary>
    Task FileChangedAsync(string userId, Guid batchId, BulkImportFileDto file, bool force);
}

/// <summary>The hub's sender (<c>/hubs/bulk-import</c>, group <c>user:{id}</c>). A singleton: the throttle is per process.</summary>
public sealed class BulkImportNotifier(IHubContext<BulkImportHub> hub, BulkImportOptions options, ILogger<BulkImportNotifier> logger)
    : IBulkImportNotifier
{
    private readonly ConcurrentDictionary<Guid, long> _lastSent = new();

    public async Task FileChangedAsync(string userId, Guid batchId, BulkImportFileDto file, bool force)
    {
        var now = Environment.TickCount64;
        if (!force && _lastSent.TryGetValue(file.FileId, out var last) && now - last < options.ProgressIntervalMs) return;
        _lastSent[file.FileId] = now;
        if (Array.IndexOf(Lilia.Core.Entities.BulkImportStatuses.FileFinished, file.Status) >= 0) _lastSent.TryRemove(file.FileId, out _);
        try
        {
            await hub.Clients.Group(BulkImportHub.GroupOf(userId)).SendAsync("BulkImportProgress", new BulkImportProgressDto(batchId, file));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[BulkImport] Could not send the progress of file {FileId}", file.FileId);
        }
    }
}
