using Lilia.Api.Directories;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>Sharing's answer to <see cref="ISharingDirectory"/>: read-only, untracked.</summary>
public sealed class SharingDirectory(LiliaDbContext db, ITeamDirectory teams) : ISharingDirectory
{
    public async Task<bool> GrantsAsync(Guid documentId, string userId, string permission, CancellationToken ct = default)
    {
        // The person's own share and the document's group shares, in one round trip.
        var grants = await db.DocumentCollaborators.AsNoTracking()
            .Where(c => c.DocumentId == documentId && c.UserId == userId)
            .Select(c => new { GroupId = (Guid?)null, c.Role.Permissions })
            .Concat(db.DocumentGroups.AsNoTracking()
                .Where(dg => dg.DocumentId == documentId)
                .Select(dg => new { GroupId = (Guid?)dg.GroupId, dg.Role.Permissions }))
            .ToListAsync(ct);
        var granting = grants.Where(g => g.Permissions.Contains(permission)).ToList();
        if (granting.Any(g => g.GroupId is null)) return true;
        if (granting.Count == 0) return false;
        // Only then ask Teams which groups the person is in.
        var mine = await teams.GroupsOfAsync(userId, ct);
        return granting.Any(g => mine.Contains(g.GroupId!.Value));
    }

    public Task<string?> RoleOfAsync(Guid documentId, string userId, CancellationToken ct = default) =>
        db.DocumentCollaborators.AsNoTracking()
            .Where(c => c.DocumentId == documentId && c.UserId == userId)
            .Select(c => c.Role.Name)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> RolesOfAsync(string userId, IEnumerable<Guid> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await db.DocumentCollaborators.AsNoTracking()
            .Where(c => ids.Contains(c.DocumentId) && c.UserId == userId)
            .Select(c => new { c.DocumentId, Role = c.Role.Name })
            .ToDictionaryAsync(c => c.DocumentId, c => c.Role, ct);
    }

    public async Task<IReadOnlyList<Guid>> DocumentsSharedWithAsync(string userId, bool throughGroups, CancellationToken ct = default)
    {
        var direct = await db.DocumentCollaborators.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.DocumentId)
            .ToListAsync(ct);
        if (!throughGroups) return direct;
        var groups = (await teams.GroupsOfAsync(userId, ct)).ToList();
        if (groups.Count == 0) return direct;
        var viaGroups = await db.DocumentGroups.AsNoTracking()
            .Where(dg => groups.Contains(dg.GroupId))
            .Select(dg => dg.DocumentId)
            .ToListAsync(ct);
        return direct.Union(viaGroups).ToList();
    }

    public async Task<IReadOnlyList<string>> PeopleSharedWithAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.DocumentCollaborators.AsNoTracking()
            .Where(c => ids.Contains(c.DocumentId))
            .Select(c => c.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<(int Documents, int People)> SharesOfAsync(IEnumerable<Guid> documentIds, string exceptUserId, CancellationToken ct = default)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0) return (0, 0);
        var shares = await db.DocumentCollaborators.AsNoTracking()
            .Where(c => ids.Contains(c.DocumentId) && c.UserId != exceptUserId)
            .Select(c => new { c.DocumentId, c.UserId })
            .ToListAsync(ct);
        return (shares.Select(s => s.DocumentId).Distinct().Count(), shares.Select(s => s.UserId).Distinct().Count());
    }

    public async Task<IReadOnlySet<string>> WithSharesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new HashSet<string>();
        return (await db.DocumentCollaborators.AsNoTracking()
            .Where(c => ids.Contains(c.UserId))
            .Select(c => c.UserId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();
    }
}
