using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// Delete account (Settings → Your data, launch redesign 3g): type the email, from a recent sign-in.
/// The account closes at once, can be restored for 30 days, then is erased: its documents and shares
/// go, and the user row is anonymised.
/// </summary>
[Collection("Integration")]
public class AccountDeletionTests : IntegrationTestBase
{
    public AccountDeletionTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string Session(DateTime at) =>
        JsonSerializer.Serialize(new { authentication_factors = new[] { new { type = "password", last_authenticated_at = at.ToString("O") } } });

    private HttpClient SignedIn(string id, DateTime signedInAt)
    {
        var c = CreateClientAs(id, $"{id}@lilia.test", "Ada");
        c.DefaultRequestHeaders.Add("X-Test-StytchSession", Session(signedInAt));
        return c;
    }

    private async Task<(string Owner, string Friend, Guid Shared)> AuthorWithASharedDocument()
    {
        var owner = $"del-{Guid.NewGuid():N}"[..20];
        var friend = $"friend-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(owner, $"{owner}@lilia.test", "Ada");
        await SeedUserAsync(friend);
        var shared = await SeedDocumentAsync(owner, "Shared paper");
        await SeedDocumentAsync(owner, "Private notes");
        var friendsOwn = await SeedDocumentAsync(friend, "Friend's paper");
        await using var db = CreateDbContext();
        var role = await db.Set<Role>().FirstAsync();
        db.DocumentCollaborators.AddRange(
            new DocumentCollaborator { Id = Guid.NewGuid(), DocumentId = shared.Id, UserId = friend, RoleId = role.Id },
            new DocumentCollaborator { Id = Guid.NewGuid(), DocumentId = friendsOwn.Id, UserId = owner, RoleId = role.Id });
        await db.SaveChangesAsync();
        return (owner, friend, friendsOwn.Id);
    }

    [Fact]
    public async Task Deleting_needs_the_email_and_a_recent_sign_in_closes_the_account_and_restore_reopens_it()
    {
        var (owner, _, _) = await AuthorWithASharedDocument();
        var client = SignedIn(owner, DateTime.UtcNow.AddMinutes(-2));

        var status = await client.GetFromJsonAsync<JsonElement>("/api/me/account");
        status.GetProperty("documents").GetInt32().Should().Be(2);
        status.GetProperty("sharedDocuments").GetInt32().Should().Be(1);
        status.GetProperty("people").GetInt32().Should().Be(1);

        (await client.PostAsJsonAsync("/api/me/delete", new { confirmEmail = "someone@else.test" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stale = SignedIn(owner, DateTime.UtcNow.AddHours(-3));
        var refused = await stale.PostAsJsonAsync("/api/me/delete", new { confirmEmail = $"{owner}@lilia.test" });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");

        var deleted = await client.PostAsJsonAsync("/api/me/delete", new { confirmEmail = $" {owner.ToUpperInvariant()}@LILIA.TEST " });
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("erasedOn").GetDateTime()
            .Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));

        var closed = await client.GetAsync("/api/documents");
        closed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await closed.Content.ReadAsStringAsync()).Should().Contain("ACCOUNT_CLOSED");
        (await client.GetAsync("/api/me/account")).StatusCode.Should().Be(HttpStatusCode.OK, "a closed account can still see its status");

        (await client.PostAsync("/api/me/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = CreateDbContext();
        (await db.Documents.CountAsync(d => d.OwnerId == owner)).Should().Be(2, "nothing is erased on request");
    }

    [Fact]
    public async Task After_30_days_the_documents_and_shares_go_and_the_user_is_anonymised()
    {
        var (owner, friend, friendsDoc) = await AuthorWithASharedDocument();
        await using (var db = CreateDbContext())
        {
            var u = await db.Users.FirstAsync(x => x.Id == owner);
            u.DeletionRequestedAt = DateTime.UtcNow.AddDays(-31);
            await db.SaveChangesAsync();
        }
        using (var scope = Fixture.Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<AccountDeletionService>().EraseDueAsync(DateTime.UtcNow)).Should().BeGreaterThanOrEqualTo(1);

        await using var check = CreateDbContext();
        (await check.Documents.IgnoreQueryFilters().CountAsync(d => d.OwnerId == owner)).Should().Be(0);
        (await check.DocumentCollaborators.CountAsync(c => c.UserId == owner)).Should().Be(0);
        (await check.Documents.AnyAsync(d => d.Id == friendsDoc)).Should().BeTrue("someone else's document stays");
        (await check.DocumentCollaborators.CountAsync(c => c.UserId == friend)).Should().Be(0, "the erased owner's shared document went with its shares");
        var erased = await check.Users.FirstAsync(u => u.Id == owner);
        erased.Name.Should().Be("Deleted user");
        erased.Email.Should().EndWith("@erased.invalid");
        erased.Image.Should().BeNull();
        erased.DeletionRequestedAt.Should().BeNull();

        (await CreateClientAs(owner).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "an erased account stays closed");
    }

    [Fact]
    public void A_sign_in_counts_as_recent_for_ten_minutes()
    {
        var now = DateTime.UtcNow;
        ClaimsPrincipal With(DateTime at) => new(new ClaimsIdentity([new Claim("https://stytch.com/session", Session(at))]));
        AccountDeletionService.RecentlySignedIn(With(now.AddMinutes(-9)), now, false).Should().BeTrue();
        AccountDeletionService.RecentlySignedIn(With(now.AddMinutes(-11)), now, false).Should().BeFalse();
        AccountDeletionService.RecentlySignedIn(new ClaimsPrincipal(new ClaimsIdentity()), now, false).Should().BeFalse("a token without Stytch's session can't prove a sign-in");
        AccountDeletionService.RecentlySignedIn(new ClaimsPrincipal(new ClaimsIdentity()), now, true).Should().BeTrue("a development identity on a test stack");
    }
}
