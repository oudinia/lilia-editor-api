namespace Lilia.Api.Directories;

/// <summary>
/// What another context may know about a team's group (2 Nov 2026, the bounded contexts, step 2): its name
/// and how many members it has, never by reading the groups tables. Teams implements it.
/// </summary>
public interface ITeamDirectory
{
    Task<GroupCard?> FindGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>The groups found among <paramref name="groupIds"/>, by id; the missing ones are left out.</summary>
    Task<IReadOnlyDictionary<Guid, GroupCard>> FindGroupsAsync(IEnumerable<Guid> groupIds, CancellationToken ct = default);
}

public sealed record GroupCard(Guid Id, string Name, int MemberCount);
