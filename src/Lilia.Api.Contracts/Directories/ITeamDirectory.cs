namespace Lilia.Api.Directories;

/// <summary>
/// What another context may know about a team's group (2 Nov 2026, the bounded contexts, step 2): its name
/// and how many members it has, never by reading the groups tables. Teams implements it.
///
/// <para>Since 3 Nov 2026 (Teams' bounded context, steps 2–3), also what documents, access, people search and
/// plan limits ask of teams: membership, team roles, peers and seats.</para>
/// </summary>
public interface ITeamDirectory
{
    Task<GroupCard?> FindGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>The groups found among <paramref name="groupIds"/>, by id; the missing ones are left out.</summary>
    Task<IReadOnlyDictionary<Guid, GroupCard>> FindGroupsAsync(IEnumerable<Guid> groupIds, CancellationToken ct = default);

    /// <summary>The groups the person is a member of.</summary>
    Task<IReadOnlySet<Guid>> GroupsOfAsync(string userId, CancellationToken ct = default);

    /// <summary>The names of the teams found among <paramref name="teamIds"/>, by id; the missing ones are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(IEnumerable<Guid> teamIds, CancellationToken ct = default);

    /// <summary>The teams the person is a member of, through any of their groups.</summary>
    Task<IReadOnlySet<Guid>> TeamsOfAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// The person's permissions in each of <paramref name="teamIds"/>, from their roles in its groups; a team
    /// they are not in is left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> PermissionsInAsync(string userId, IEnumerable<Guid> teamIds, CancellationToken ct = default);

    /// <summary>Whether the person is a member of the team, or its owner.</summary>
    Task<bool> BelongsToAsync(string userId, Guid teamId, CancellationToken ct = default);

    /// <summary>Everyone in a group with the person, the person included.</summary>
    Task<IReadOnlySet<string>> PeersOfAsync(string userId, CancellationToken ct = default);

    /// <summary>The distinct members of the teams the person owns, sandbox teams aside: the seats their plan counts.</summary>
    Task<int> SeatsOwnedByAsync(string ownerId, CancellationToken ct = default);
}

public sealed record GroupCard(Guid Id, string Name, int MemberCount);
