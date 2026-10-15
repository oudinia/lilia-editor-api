using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// Settings → Profile (launch redesign 3a): the name collaborators see is the author's to set, and a
/// sign-in whose token carries no name doesn't wipe it.
/// </summary>
[Collection("Integration")]
public class ProfileTests : IntegrationTestBase
{
    public ProfileTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_author_reads_and_changes_the_name_others_see()
    {
        var id = $"profile-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test", "Old Name");
        var client = CreateClientAs(id, $"{id}@lilia.test", "Old Name");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me/profile");
        me.GetProperty("name").GetString().Should().Be("Old Name");
        me.GetProperty("email").GetString().Should().Be($"{id}@lilia.test");

        var put = await client.PutAsJsonAsync("/api/me/profile", new { name = "  Ada Lovelace  " });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Ada Lovelace");

        (await client.PutAsJsonAsync("/api/me/profile", new { name = "   " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/me/profile", new { name = new string('x', 121) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        (await db.Users.FirstAsync(u => u.Id == id)).Name.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task A_sign_in_without_a_name_keeps_the_name_set_in_settings()
    {
        var id = $"profile-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test", "Chosen Name");
        using var scope = Fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();

        await users.CreateOrUpdateUserAsync(new CreateOrUpdateUserDto(id, $"{id}@lilia.test", null, null));

        await using var db = CreateDbContext();
        (await db.Users.FirstAsync(u => u.Id == id)).Name.Should().Be("Chosen Name");
    }
}
