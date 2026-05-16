using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Events.Common;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wolverine;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// End-to-end flow tests for team membership — invite (existing user
/// + invite by email for unknown user) and remove — including
/// assertion that the Wolverine email events publish at the right
/// moments. Mirrors the contract the editor's /teams page depends on.
///
/// The IMessageBus is mocked so we observe published events without
/// running the actual Wolverine handlers (which would try to send
/// real emails via Resend in test).
/// </summary>
[Collection("Integration")]
public class TeamMembershipFlowTests : IntegrationTestBase
{
    public TeamMembershipFlowTests(TestDatabaseFixture fixture) : base(fixture) { }

    // ── Test data ─────────────────────────────────────────────────────
    private const string OwnerId = "kp_test_owner";
    private const string OwnerEmail = "owner@lilia.test";
    private const string OwnerName = "Test Owner";
    private const string ExistingMemberId = "kp_test_member";
    private const string ExistingMemberEmail = "member@lilia.test";
    private const string ExistingMemberName = "Test Member";
    private const string UnknownEmail = "newperson@example.test";

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Build a factory that swaps IMessageBus for a Mock so each test
    /// can verify what events the controller / service published. Also
    /// returns the mock so callers can assert on it.
    ///
    /// <paramref name="useRealUserService"/> swaps the test factory's
    /// NoOpUserService back to the real one — required when the test
    /// needs <c>GetUserByEmailAsync</c> to actually resolve seeded
    /// users (e.g. invite-existing-user path).
    /// </summary>
    private (WebApplicationFactory<Program> factory, Mock<IMessageBus> bus) WithMockBus(bool useRealUserService = false)
    {
        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<TeamInviteCreatedEvent>(), It.IsAny<DeliveryOptions>()))
               .Returns(ValueTask.CompletedTask);
        busMock.Setup(b => b.PublishAsync(It.IsAny<TeamMemberRemovedEvent>(), It.IsAny<DeliveryOptions>()))
               .Returns(ValueTask.CompletedTask);

        var factory = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IMessageBus));
                if (existing != null) services.Remove(existing);
                services.AddScoped(_ => busMock.Object);

                if (useRealUserService)
                {
                    // The default test factory registers NoOpUserService
                    // (always-null GetUserByEmailAsync) to skip
                    // UserSyncMiddleware in tests. For the
                    // invite-existing-user path we need the real one
                    // so the seeded user is found by email.
                    var noOp = services.SingleOrDefault(d => d.ServiceType == typeof(IUserService));
                    if (noOp != null) services.Remove(noOp);
                    services.AddScoped<IUserService, UserService>();
                }
            });
        });
        return (factory, busMock);
    }

    /// <summary>
    /// Seed a team with default Group + ensure the "member" role exists
    /// (TeamService.InviteMemberAsync looks both up). Owner is added as
    /// owner-role group member so they pass the access check.
    /// </summary>
    private async Task<Team> SeedTeamWithOwnerAsync(string ownerId)
    {
        await using var db = CreateDbContext();
        // Ensure roles exist.
        foreach (var name in new[] { "owner", "admin", "member", "viewer" })
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name))
            {
                db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = name });
            }
        }
        await db.SaveChangesAsync();

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Test Team",
            TeamCode = "Test Team",
            Slug = "test-team-" + Guid.NewGuid().ToString("N")[..8],
            OwnerId = ownerId,
            Plan = "free",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var group = new Group
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Name = "Everyone",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
        };
        team.Groups.Add(group);

        var ownerRole = await db.Roles.FirstAsync(r => r.Name == "owner");
        var ownerMembership = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = ownerId,
            RoleId = ownerRole.Id,
            CreatedAt = DateTime.UtcNow,
        };
        group.Members.Add(ownerMembership);

        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return team;
    }

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_existing_user_adds_membership_and_publishes_event()
    {
        await SeedUserAsync(OwnerId, OwnerEmail, OwnerName);
        await SeedUserAsync(ExistingMemberId, ExistingMemberEmail, ExistingMemberName);
        var team = await SeedTeamWithOwnerAsync(OwnerId);

        // useRealUserService: true → GetUserByEmailAsync actually
        // resolves the seeded ExistingMember instead of always
        // returning null (which would push us into the pending path).
        var (factory, bus) = WithMockBus(useRealUserService: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", OwnerId);
        client.DefaultRequestHeaders.Add("X-Test-Email", OwnerEmail);
        client.DefaultRequestHeaders.Add("X-Test-Name", OwnerName);

        var response = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new { email = ExistingMemberEmail, role = "member" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Membership row exists
        await using var db = CreateDbContext();
        var hasMember = await db.GroupMembers.AnyAsync(gm =>
            gm.Group.TeamId == team.Id && gm.UserId == ExistingMemberId);
        hasMember.Should().BeTrue("the invitee should be added to the team's default group");

        // The Wolverine event went out — the email handler will send
        // the "X invited you" mail in the real pipeline.
        bus.Verify(b => b.PublishAsync(
            It.Is<TeamInviteCreatedEvent>(e =>
                e.TeamId == team.Id &&
                e.InvitedEmail == ExistingMemberEmail &&
                e.Role == "member"),
            It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Invite_unknown_email_returns_pending_stub_and_still_publishes_event()
    {
        // No SeedUserAsync for UnknownEmail — they don't have an
        // account yet. Pre-fix the controller returned 404 here. Now
        // it returns 200 with a `pending:<email>` UserId stub and
        // still publishes the invite event so an email goes out.
        await SeedUserAsync(OwnerId, OwnerEmail, OwnerName);
        var team = await SeedTeamWithOwnerAsync(OwnerId);

        var (factory, bus) = WithMockBus();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", OwnerId);
        client.DefaultRequestHeaders.Add("X-Test-Email", OwnerEmail);
        client.DefaultRequestHeaders.Add("X-Test-Name", OwnerName);

        var response = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new { email = UnknownEmail, role = "member" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"pending:{UnknownEmail}");
        body.Should().Contain(UnknownEmail);

        // No actual GroupMember row created — they don't exist as a
        // user yet, so they can't be added to a group. The pending
        // state lives only in the email + the controller response.
        await using var db = CreateDbContext();
        var memberRowCount = await db.GroupMembers.CountAsync(gm => gm.Group.TeamId == team.Id);
        memberRowCount.Should().Be(1, "only the owner should be a group member at this point");

        // Email event still fires.
        bus.Verify(b => b.PublishAsync(
            It.Is<TeamInviteCreatedEvent>(e =>
                e.TeamId == team.Id &&
                e.InvitedEmail == UnknownEmail),
            It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Remove_member_drops_membership_and_publishes_event()
    {
        await SeedUserAsync(OwnerId, OwnerEmail, OwnerName);
        await SeedUserAsync(ExistingMemberId, ExistingMemberEmail, ExistingMemberName);
        var team = await SeedTeamWithOwnerAsync(OwnerId);

        // Add the member directly to the default group so we can
        // exercise the remove path without re-using the invite path.
        await using (var db = CreateDbContext())
        {
            var group = await db.Groups.FirstAsync(g => g.TeamId == team.Id && g.IsDefault);
            var role = await db.Roles.FirstAsync(r => r.Name == "member");
            db.GroupMembers.Add(new GroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                UserId = ExistingMemberId,
                RoleId = role.Id,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (factory, bus) = WithMockBus();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", OwnerId);
        client.DefaultRequestHeaders.Add("X-Test-Email", OwnerEmail);
        client.DefaultRequestHeaders.Add("X-Test-Name", OwnerName);

        var response = await client.DeleteAsync($"/api/teams/{team.Id}/members/{ExistingMemberId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var checkDb = CreateDbContext();
        var hasMember = await checkDb.GroupMembers.AnyAsync(gm =>
            gm.Group.TeamId == team.Id && gm.UserId == ExistingMemberId);
        hasMember.Should().BeFalse("removed member should no longer have a group_members row");

        bus.Verify(b => b.PublishAsync(
            It.Is<TeamMemberRemovedEvent>(e =>
                e.TeamId == team.Id &&
                e.RemovedUserId == ExistingMemberId &&
                e.RemovedUserEmail == ExistingMemberEmail &&
                e.RemovedByUserId == OwnerId),
            It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Cannot_remove_team_owner_and_no_event_fires()
    {
        // Self-removal of the owner is forbidden by the service
        // (returns false → controller 404). No event should fire.
        await SeedUserAsync(OwnerId, OwnerEmail, OwnerName);
        var team = await SeedTeamWithOwnerAsync(OwnerId);

        var (factory, bus) = WithMockBus();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", OwnerId);
        client.DefaultRequestHeaders.Add("X-Test-Email", OwnerEmail);
        client.DefaultRequestHeaders.Add("X-Test-Name", OwnerName);

        var response = await client.DeleteAsync($"/api/teams/{team.Id}/members/{OwnerId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        bus.Verify(b => b.PublishAsync(It.IsAny<TeamMemberRemovedEvent>(), It.IsAny<DeliveryOptions>()), Times.Never);
    }

    [Fact]
    public async Task Non_owner_cannot_invite_or_remove()
    {
        // A non-owner authenticated user should be rejected with 404
        // (the service hides team existence from non-owners by lumping
        // "not your team" and "no such team" into the same null return).
        await SeedUserAsync(OwnerId, OwnerEmail, OwnerName);
        await SeedUserAsync(ExistingMemberId, ExistingMemberEmail, ExistingMemberName);
        var team = await SeedTeamWithOwnerAsync(OwnerId);

        var (factory, bus) = WithMockBus();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", ExistingMemberId);
        client.DefaultRequestHeaders.Add("X-Test-Email", ExistingMemberEmail);
        client.DefaultRequestHeaders.Add("X-Test-Name", ExistingMemberName);

        var inviteResp = await client.PostAsJsonAsync(
            $"/api/teams/{team.Id}/members",
            new { email = UnknownEmail, role = "member" });
        inviteResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var removeResp = await client.DeleteAsync($"/api/teams/{team.Id}/members/{OwnerId}");
        removeResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        bus.Verify(b => b.PublishAsync(It.IsAny<TeamInviteCreatedEvent>(), It.IsAny<DeliveryOptions>()), Times.Never);
        bus.Verify(b => b.PublishAsync(It.IsAny<TeamMemberRemovedEvent>(), It.IsAny<DeliveryOptions>()), Times.Never);
    }
}
