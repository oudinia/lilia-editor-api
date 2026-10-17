using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// Which version of the Terms an author accepted (launch redesign 2c, gate A): recorded at sign-up
/// and with OK on the notice that the Terms changed; never lowered; nobody else's.
/// </summary>
[Collection("Integration")]
public class LegalAcceptanceTests : IntegrationTestBase
{
    public LegalAcceptanceTests(TestDatabaseFixture fixture) : base(fixture) { }

    // The API leaves null fields out of its JSON: no "version" means none accepted yet.
    private static int? Version(JsonElement e) =>
        e.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    [Fact]
    public async Task A_new_account_has_none_until_it_accepts_then_the_version_and_date_are_kept()
    {
        var id = $"legal-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test", "Ada");
        var client = CreateClientAs(id, $"{id}@lilia.test", "Ada");

        var before = await client.GetFromJsonAsync<JsonElement>("/api/me/legal");
        Version(before).Should().BeNull();

        var post = await client.PostAsJsonAsync("/api/me/legal", new { version = 1 });
        post.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/me/legal");
        after.GetProperty("version").GetInt32().Should().Be(1);
        var acceptedAt = after.GetProperty("acceptedAt").GetDateTime();
        acceptedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Accepting the same version again keeps the first date.
        await client.PostAsJsonAsync("/api/me/legal", new { version = 1 });
        (await client.GetFromJsonAsync<JsonElement>("/api/me/legal")).GetProperty("acceptedAt").GetDateTime().Should().Be(acceptedAt);

        await using var db = CreateDbContext();
        (await db.Users.FirstAsync(u => u.Id == id)).LegalAcceptedVersion.Should().Be(1);
    }

    [Fact]
    public async Task A_newer_version_replaces_it_and_an_older_one_never_lowers_it()
    {
        var id = $"legal-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test", "Ada");
        var client = CreateClientAs(id, $"{id}@lilia.test", "Ada");

        await client.PostAsJsonAsync("/api/me/legal", new { version = 2 });
        var older = await client.PostAsJsonAsync("/api/me/legal", new { version = 1 });
        older.StatusCode.Should().Be(HttpStatusCode.OK, "an old tab's OK is harmless");
        (await older.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32().Should().Be(2);

        await client.PostAsJsonAsync("/api/me/legal", new { version = 3 });
        (await client.GetFromJsonAsync<JsonElement>("/api/me/legal")).GetProperty("version").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task A_version_out_of_range_is_refused_and_an_anonymous_call_is_not_allowed()
    {
        var id = $"legal-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test", "Ada");
        var client = CreateClientAs(id, $"{id}@lilia.test", "Ada");
        (await client.PostAsJsonAsync("/api/me/legal", new { version = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/me/legal", new { version = 1000 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var anonymous = CreateAnonymousClient();
        (await anonymous.GetAsync("/api/me/legal")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/me/legal", new { version = 1 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_authors_acceptance_does_not_touch_anothers()
    {
        var a = $"legal-{Guid.NewGuid():N}"[..20];
        var b = $"legal-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(a, $"{a}@lilia.test", "A");
        await SeedUserAsync(b, $"{b}@lilia.test", "B");
        await CreateClientAs(a, $"{a}@lilia.test", "A").PostAsJsonAsync("/api/me/legal", new { version = 1 });
        var other = await CreateClientAs(b, $"{b}@lilia.test", "B").GetFromJsonAsync<JsonElement>("/api/me/legal");
        Version(other).Should().BeNull();
    }
}
