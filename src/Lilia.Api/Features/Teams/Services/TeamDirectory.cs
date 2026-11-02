using Lilia.Api.Directories;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Services;

/// <summary>Teams' answer to <see cref="ITeamDirectory"/>: read-only, untracked.</summary>
public sealed class TeamDirectory(LiliaDbContext db) : ITeamDirectory
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
}
