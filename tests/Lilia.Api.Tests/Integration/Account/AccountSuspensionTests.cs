using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// An account suspended from the admin console (Banned, BanReason "suspended") is refused by the
/// API with ACCOUNT_SUSPENDED; a suspension that has expired is not.
/// </summary>
[Collection("Integration")]
public class AccountSuspensionTests : IntegrationTestBase
{
    public AccountSuspensionTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<HttpClient> UserAsync(string tag, bool banned, DateTime? expires = null)
    {
        var id = $"susp-{tag}-{Guid.NewGuid():N}"[..24];
        await SeedUserAsync(id, $"{id}@lilia.test", "Someone");
        if (banned)
        {
            await using var db = CreateDbContext();
            var u = await db.Users.SingleAsync(x => x.Id == id);
            u.Banned = true; u.BanReason = "suspended"; u.BanExpires = expires;
            await db.SaveChangesAsync();
        }
        return CreateClientAs(id, $"{id}@lilia.test", "Someone");
    }

    [Fact]
    public async Task A_suspended_account_is_refused_with_its_own_code()
    {
        var r = await (await UserAsync("on", banned: true)).GetAsync("/api/documents");
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadAsStringAsync()).Should().Contain("ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task An_account_that_is_not_suspended_or_whose_suspension_expired_gets_in()
    {
        (await (await UserAsync("off", banned: false)).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await UserAsync("expired", banned: true, DateTime.UtcNow.AddDays(-1))).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
