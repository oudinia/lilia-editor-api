using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Features.BulkImport.Hubs;

/// <summary>
/// Bulk import's live progress (<c>/hubs/bulk-import</c>, 21 Nov 2026). Signed in as the other hubs are (the token in the
/// query string, Program.cs); each connection joins its author's group, <c>user:{id}</c>, which gets
/// <c>BulkImportProgress</c> <c>{ batchId, file }</c> for every file of every batch of theirs. Nothing to call: an
/// author sees only their own imports.
/// </summary>
[Authorize]
public sealed class BulkImportHub : Hub
{
    public static string GroupOf(string userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(userId));
        await base.OnConnectedAsync();
    }
}
