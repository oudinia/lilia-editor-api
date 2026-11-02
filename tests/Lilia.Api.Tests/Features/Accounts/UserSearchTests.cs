using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Accounts;

/// <summary>
/// The user search had no test (2 Nov 2026, the coverage round). Its privacy rules (lilia-docs
/// specs/sharing-and-notifications.md): only people who already share a document or team with you,
/// by name only, never by email, no email in the answer, ten searches a minute.
/// </summary>
public class UserSearchTests : IntegrationTestBase
{
    public UserSearchTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task ShareAsync(string owner, string with)
    {
        var doc = await SeedDocumentAsync(owner, "Shared");
        await using var db = CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Viewer)
            ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = RoleNames.Viewer, Permissions = [Permissions.Read] }).Entity;
        db.DocumentCollaborators.Add(new DocumentCollaborator
        {
            Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = with, RoleId = role.Id, InvitedBy = owner, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Finds_only_people_you_share_with_by_name_and_shows_no_email()
    {
        await SeedUserAsync("us_me", "me@lilia.test", "Ada Me");
        await SeedUserAsync("us_peer", "grace.peer@lilia.test", "Grace Hopper");
        await SeedUserAsync("us_stranger", "grace.stranger@lilia.test", "Grace Stranger");
        await ShareAsync("us_me", "us_peer");
        using var c = CreateClientAs("us_me");

        var r = await c.GetAsync("/api/users/search?q=grace");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadAsStringAsync();
        body.Should().Contain("us_peer").And.Contain("Grace Hopper");
        body.Should().NotContain("us_stranger", "a person who shares nothing with you is not findable");
        body.Should().NotContain("@lilia.test", "the answer never carries an email");

        // An email-shaped query finds nothing, even a peer's exact address.
        (await c.GetStringAsync("/api/users/search?q=grace.peer@lilia.test")).Should().Be("[]");
    }

    [Fact]
    public async Task The_eleventh_search_in_a_minute_is_refused()
    {
        await SeedUserAsync("us_busy");
        using var c = CreateClientAs("us_busy");
        for (var i = 0; i < 10; i++)
            (await c.GetAsync($"/api/users/search?q=ab{i}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await c.GetAsync("/api/users/search?q=abc");
        r.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        r.Headers.RetryAfter.Should().NotBeNull();
    }
}
