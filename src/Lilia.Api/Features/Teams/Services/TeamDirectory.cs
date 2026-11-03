using Lilia.Api.Directories;
using Lilia.Api.Features.Teams.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Services;

/// <summary>Teams' answer to <see cref="ITeamDirectory"/>: read-only, untracked.</summary>
public sealed class TeamDirectory(TeamsDbContext db) : ITeamDirectory
{
    public Task<GroupCard?> FindGroupAsync(Guid groupId, CancellationToken ct = default) =>
        db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new GroupCard(g.Id, g.Name, g.Members.Count))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, GroupCard>> FindGroupsAsync(IEnumerable<Guid> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, GroupCard>();
        return await db.Groups.AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .Select(g => new GroupCard(g.Id, g.Name, g.Members.Count))
            .ToDictionaryAsync(g => g.Id, ct);
    }

    public async Task<IReadOnlySet<Guid>> GroupsOfAsync(string userId, CancellationToken ct = default) =>
        (await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .ToListAsync(ct)).ToHashSet();

    // Moved unchanged from DocumentService, DocumentAccess, UsersController and EntitlementService (3 Nov 2026).

    public async Task<IReadOnlySet<Guid>> TeamsOfAsync(string userId, CancellationToken ct = default) =>
        (await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.Group.TeamId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> PermissionsInAsync(string userId, IEnumerable<Guid> teamIds, CancellationToken ct = default)
    {
        var ids = teamIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, IReadOnlySet<string>>();
        var rows = await db.GroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.Group.TeamId) && m.UserId == userId)
            .Select(m => new { m.Group.TeamId, m.Role.Permissions })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.SelectMany(r => r.Permissions).ToHashSet());
    }

    public async Task<bool> BelongsToAsync(string userId, Guid teamId, CancellationToken ct = default) =>
        // Membership is a row in any of the team's groups (the default "Everyone" group at minimum), or the
        // team's OwnerId: the old team_members table was retired (RetireTeamMembersTable).
        await db.GroupMembers.AnyAsync(m => m.Group.TeamId == teamId && m.UserId == userId, ct)
        || await db.Teams.AnyAsync(t => t.Id == teamId && t.OwnerId == userId, ct);

    public async Task<IReadOnlySet<string>> PeersOfAsync(string userId, CancellationToken ct = default)
    {
        var myGroups = db.GroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId);
        return (await db.GroupMembers.AsNoTracking()
            .Where(m => myGroups.Contains(m.GroupId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();
    }

    public Task<int> SeatsOwnedByAsync(string ownerId, CancellationToken ct = default) =>
        db.Teams.AsNoTracking()
            // FT-SANDBOX-SCOPE: sandbox teams' seats don't count against quota.
            .Where(t => t.OwnerId == ownerId && !t.IsPlayground)
            .SelectMany(t => t.Groups.SelectMany(g => g.Members))
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(ct);
}
