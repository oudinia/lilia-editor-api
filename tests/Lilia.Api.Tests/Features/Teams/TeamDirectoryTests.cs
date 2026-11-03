using FluentAssertions;
using Lilia.Api.Directories;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Teams;

/// <summary>
/// What Teams answers the other contexts (3 Nov 2026, Teams' bounded context, step 2): the teams a person is
/// in, their permissions per team, whether they belong to one, their peers, and an owner's seats.
/// </summary>
public class TeamDirectoryTests : IntegrationTestBase
{
    // The roles seeded by migration (owner, editor, viewer).
    private static readonly Guid Editor = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Viewer = Guid.Parse("00000000-0000-0000-0000-000000000003");

    public TeamDirectoryTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid Team, Guid Group)> SeedTeamAsync(string ownerId, bool playground = false)
    {
        await using var db = CreateDbContext();
        var team = new Team { Id = Guid.NewGuid(), Name = $"team-{ownerId}", TeamCode = Guid.NewGuid().ToString("N")[..8], OwnerId = ownerId, IsPlayground = playground };
        var group = new Group { Id = Guid.NewGuid(), TeamId = team.Id, Name = "Everyone", IsDefault = true };
        db.Teams.Add(team);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return (team.Id, group.Id);
    }

    private async Task JoinAsync(Guid groupId, string userId, Guid roleId)
    {
        await using var db = CreateDbContext();
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    private ITeamDirectory Directory() => Fixture.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ITeamDirectory>();

    [Fact]
    public async Task It_answers_membership_roles_peers_and_seats()
    {
        foreach (var u in new[] { "td-owner", "td-ada", "td-grace", "td-stranger" }) await SeedUserAsync(u);
        var (team, group) = await SeedTeamAsync("td-owner");
        var (sandbox, sandboxGroup) = await SeedTeamAsync("td-owner", playground: true);
        await JoinAsync(group, "td-ada", Editor);
        await JoinAsync(group, "td-grace", Viewer);
        await JoinAsync(sandboxGroup, "td-stranger", Viewer);
        var teams = Directory();

        (await teams.TeamsOfAsync("td-ada")).Should().BeEquivalentTo([team]);
        (await teams.TeamsOfAsync("td-owner")).Should().BeEmpty("owning a team is not being in one of its groups");

        var ada = await teams.PermissionsInAsync("td-ada", [team, sandbox]);
        ada.Keys.Should().BeEquivalentTo([team], "a team she is not in is left out");
        ada[team].Should().Contain(Permissions.Write);
        (await teams.PermissionsInAsync("td-grace", [team]))[team].Should().NotContain(Permissions.Write);
        (await teams.PermissionsInAsync("td-ada", [])).Should().BeEmpty();

        (await teams.BelongsToAsync("td-ada", team)).Should().BeTrue();
        (await teams.BelongsToAsync("td-owner", team)).Should().BeTrue("the owner belongs without a membership row");
        (await teams.BelongsToAsync("td-stranger", team)).Should().BeFalse();

        (await teams.PeersOfAsync("td-ada")).Should().BeEquivalentTo(["td-ada", "td-grace"]);
        (await teams.PeersOfAsync("td-owner")).Should().BeEmpty();

        (await teams.SeatsOwnedByAsync("td-owner")).Should().Be(2, "the sandbox team's member doesn't count");
    }

    [Fact]
    public async Task A_default_team_is_claimed_once()
    {
        await SeedUserAsync("td-claim");
        var (first, _) = await SeedTeamAsync("td-claim");
        var (second, _) = await SeedTeamAsync("td-claim");
        var accounts = Fixture.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ITeamAccounts>();

        (await accounts.DefaultTeamOfAsync("td-claim")).Should().BeNull();
        (await accounts.ClaimDefaultTeamAsync("td-claim", first)).Should().BeTrue();
        (await accounts.ClaimDefaultTeamAsync("td-claim", second)).Should().BeFalse("a second sign-up event keeps the first team");
        (await accounts.DefaultTeamOfAsync("td-claim")).Should().Be(first);
        (await accounts.ClaimDefaultTeamAsync("td-nobody", first)).Should().BeFalse();
    }
}
