using Lilia.Api.Directories;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>Documents' answer to <see cref="IDocumentAccess"/>: moved unchanged from DocumentService.HasAccessAsync (3 Nov 2026).</summary>
public sealed class DocumentAccess(LiliaDbContext context, ISharingDirectory sharing) : IDocumentAccess
{
    /// <summary>A team grants reading and writing; deleting, managing the sharing and transferring stay with the owner.</summary>
    private static readonly HashSet<string> TeamGrantable = [Permissions.Read, Permissions.Write];

    public async Task<bool> CanAsync(Guid documentId, string userId, string permission, CancellationToken ct = default)
    {
        var document = await context.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.OwnerId, d.TeamId })
            .FirstOrDefaultAsync(ct);

        if (document == null) return false;

        // Owner always has full access
        if (document.OwnerId == userId) return true;

        // A share with the person, or with a group they are in: Sharing's answer.
        if (await sharing.GrantsAsync(documentId, userId, permission, ct))
            return true;

        // Team access: a document put in a team is open to the team's
        // members, at their team role. It stops at editing: deleting,
        // managing the sharing and transferring stay with the owner.
        if (document.TeamId is { } teamId && TeamGrantable.Contains(permission))
        {
            var teamRoles = await context.GroupMembers
                .Where(gm => gm.Group.TeamId == teamId && gm.UserId == userId)
                .Select(gm => gm.Role.Permissions)
                .ToListAsync(ct);
            if (teamRoles.Any(p => p.Contains(permission))) return true;
        }

        return false;
    }
}
