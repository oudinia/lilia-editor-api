using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data.Seeds;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Snippets;

/// <summary>
/// A system snippet is one row shared by every user. Favouriting it flipped
/// the flag on that row, so it became a favourite for everyone (found by the
/// e2e run with a second user). Favourites of system snippets are per user.
/// </summary>
[Collection("Integration")]
public class SnippetFavoritesTests : IntegrationTestBase
{
    private const string Alice = "test_user_001";
    private const string Bob = "test_user_002";

    public SnippetFavoritesTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Snippet> ASystemSnippetAsync()
    {
        await using var db = CreateDbContext();
        if (!await db.Snippets.AnyAsync(s => s.IsSystem))
            await SystemSnippetSeeder.SeedAsync(db);
        return await db.Snippets.Where(s => s.IsSystem).OrderBy(s => s.Name).FirstAsync();
    }

    private static async Task<SnippetDto?> FindInListAsync(HttpClient client, Guid id, bool favoritesOnly = false)
    {
        var page = await client.GetFromJsonAsync<SnippetPageDto>(
            $"/api/snippets?pageSize=100&favoritesOnly={favoritesOnly.ToString().ToLowerInvariant()}");
        return page!.Items.SingleOrDefault(s => s.Id == id);
    }

    [Fact]
    public async Task Favouriting_a_system_snippet_makes_it_a_favourite_for_that_user_only()
    {
        await SeedUserAsync(Alice);
        await SeedUserAsync(Bob);
        var snippet = await ASystemSnippetAsync();
        using var alice = CreateClientAs(Alice);
        using var bob = CreateClientAs(Bob);

        var toggle = await alice.PostAsync($"/api/snippets/{snippet.Id}/favorite", null);
        toggle.StatusCode.Should().Be(HttpStatusCode.OK);
        (await toggle.Content.ReadFromJsonAsync<SnippetDto>())!.IsFavorite.Should().BeTrue();

        (await FindInListAsync(alice, snippet.Id))!.IsFavorite.Should().BeTrue();
        (await FindInListAsync(alice, snippet.Id, favoritesOnly: true)).Should().NotBeNull();
        (await alice.GetFromJsonAsync<SnippetDto>($"/api/snippets/{snippet.Id}"))!.IsFavorite.Should().BeTrue();

        (await FindInListAsync(bob, snippet.Id))!.IsFavorite.Should().BeFalse("Alice's favourite is not Bob's");
        (await FindInListAsync(bob, snippet.Id, favoritesOnly: true)).Should().BeNull();
        (await bob.GetFromJsonAsync<SnippetDto>($"/api/snippets/{snippet.Id}"))!.IsFavorite.Should().BeFalse();

        await using var db = CreateDbContext();
        (await db.Snippets.SingleAsync(s => s.Id == snippet.Id)).IsFavorite.Should().BeFalse("the shared row is not touched");
    }

    [Fact]
    public async Task Unfavouriting_a_system_snippet_leaves_other_users_favourite_alone()
    {
        await SeedUserAsync(Alice);
        await SeedUserAsync(Bob);
        var snippet = await ASystemSnippetAsync();
        using var alice = CreateClientAs(Alice);
        using var bob = CreateClientAs(Bob);

        (await alice.PostAsync($"/api/snippets/{snippet.Id}/favorite", null)).EnsureSuccessStatusCode();
        (await bob.PostAsync($"/api/snippets/{snippet.Id}/favorite", null)).EnsureSuccessStatusCode();

        var unfavourite = await alice.PostAsync($"/api/snippets/{snippet.Id}/favorite", null);
        (await unfavourite.Content.ReadFromJsonAsync<SnippetDto>())!.IsFavorite.Should().BeFalse();

        (await FindInListAsync(alice, snippet.Id))!.IsFavorite.Should().BeFalse();
        (await FindInListAsync(bob, snippet.Id))!.IsFavorite.Should().BeTrue("Bob favourited it himself");
    }

    [Fact]
    public async Task A_users_own_snippet_is_favourited_as_before()
    {
        await SeedUserAsync(Alice);
        using var alice = CreateClientAs(Alice);
        var created = await alice.PostAsJsonAsync("/api/snippets", new
        {
            name = "My table",
            latexContent = "\\begin{tabular}{l}x\\end{tabular}",
            blockType = "table",
            category = "tables",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var mine = (await created.Content.ReadFromJsonAsync<SnippetDto>())!;

        var toggle = await alice.PostAsync($"/api/snippets/{mine.Id}/favorite", null);
        (await toggle.Content.ReadFromJsonAsync<SnippetDto>())!.IsFavorite.Should().BeTrue();
        (await FindInListAsync(alice, mine.Id, favoritesOnly: true))!.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task A_favourite_survives_the_system_snippets_being_seeded_again()
    {
        // The seeder runs at every start. It used to delete and re-add every
        // system snippet under new ids, which would drop every favourite.
        await SeedUserAsync(Alice);
        var snippet = await ASystemSnippetAsync();
        using var alice = CreateClientAs(Alice);
        (await alice.PostAsync($"/api/snippets/{snippet.Id}/favorite", null)).EnsureSuccessStatusCode();

        List<Guid> idsBefore;
        await using (var db = CreateDbContext())
        {
            idsBefore = await db.Snippets.Where(s => s.IsSystem).Select(s => s.Id).OrderBy(i => i).ToListAsync();
            await SystemSnippetSeeder.SeedAsync(db);
        }

        await using (var db = CreateDbContext())
        {
            (await db.Snippets.Where(s => s.IsSystem).Select(s => s.Id).OrderBy(i => i).ToListAsync())
                .Should().Equal(idsBefore);
        }
        (await FindInListAsync(alice, snippet.Id))!.IsFavorite.Should().BeTrue();
    }
}
