using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Tables;

/// <summary>
/// Tables have their own Trash. A deleted table was only ever soft-deleted and
/// then unreachable; now it can be listed, restored, or deleted for good.
/// </summary>
/// <remarks>
/// Each test uses its own user: cleanup does not delete tables, and the trash
/// listing is per user, so a shared author would see other tests' tables.
/// </remarks>
[Collection("Integration")]
public class TablesTrashTests : IntegrationTestBase
{
    public TablesTrashTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string NewUser() => $"test_user_tables_trash_{Guid.NewGuid():N}";

    private static object Body(string caption) => new
    {
        caption,
        label = "tab:trash",
        content = new { headers = new[] { "Model", "Top-1" }, rows = new[] { new[] { "ResNet", "76.1" } } },
    };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage res) =>
        await res.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateTableAsync(HttpClient client, string caption)
    {
        var res = await client.PostAsJsonAsync("/api/tables", Body(caption));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await JsonOf(res)).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await JsonOf(res)).EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task A_deleted_table_is_in_the_trash_and_not_in_the_list()
    {
        var user = NewUser();
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        var tableId = await CreateTableAsync(client, "Top-1");

        (await client.DeleteAsync($"/api/tables/{tableId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await IdsAsync(client, "/api/tables")).Should().NotContain(tableId);
        var trash = (await JsonOf(await client.GetAsync("/api/tables/trash"))).EnumerateArray().ToList();
        var row = trash.Should().ContainSingle().Subject;
        row.GetProperty("id").GetGuid().Should().Be(tableId);
        row.GetProperty("caption").GetString().Should().Be("Top-1");
        row.GetProperty("label").GetString().Should().Be("tab:trash");
        row.GetProperty("deletedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        row.TryGetProperty("updatedAt", out _).Should().BeTrue();
        row.GetProperty("documentCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task The_trash_lists_the_newest_deletion_first()
    {
        var user = NewUser();
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        var first = await CreateTableAsync(client, "First");
        var second = await CreateTableAsync(client, "Second");

        await client.DeleteAsync($"/api/tables/{first}");
        await client.DeleteAsync($"/api/tables/{second}");

        (await IdsAsync(client, "/api/tables/trash")).Should().Equal(second, first);
    }

    [Fact]
    public async Task A_restored_table_is_back_in_the_list_and_gone_from_the_trash()
    {
        var user = NewUser();
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        var tableId = await CreateTableAsync(client, "Top-1");
        await client.DeleteAsync($"/api/tables/{tableId}");

        (await client.PostAsync($"/api/tables/{tableId}/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await IdsAsync(client, "/api/tables")).Should().Contain(tableId);
        (await IdsAsync(client, "/api/tables/trash")).Should().NotContain(tableId);
        (await client.PostAsync($"/api/tables/{tableId}/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a live table is not in the trash");
    }

    [Fact]
    public async Task A_permanently_deleted_table_is_gone_from_both_and_a_second_purge_is_404()
    {
        var user = NewUser();
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        var tableId = await CreateTableAsync(client, "Top-1");
        await client.DeleteAsync($"/api/tables/{tableId}");

        (await client.DeleteAsync($"/api/tables/{tableId}/permanent"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await IdsAsync(client, "/api/tables")).Should().NotContain(tableId);
        (await IdsAsync(client, "/api/tables/trash")).Should().NotContain(tableId);
        await using (var db = CreateDbContext())
            (await db.Tables.IgnoreQueryFilters().AnyAsync(t => t.Id == tableId)).Should().BeFalse();
        (await client.DeleteAsync($"/api/tables/{tableId}/permanent"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_user_cannot_see_restore_or_purge_it()
    {
        var owner = NewUser();
        var stranger = NewUser();
        await SeedUserAsync(owner);
        await SeedUserAsync(stranger);
        using var ownerClient = CreateClientAs(owner);
        using var strangerClient = CreateClientAs(stranger);
        var tableId = await CreateTableAsync(ownerClient, "Top-1");
        await ownerClient.DeleteAsync($"/api/tables/{tableId}");

        (await IdsAsync(strangerClient, "/api/tables/trash")).Should().NotContain(tableId);
        (await strangerClient.PostAsync($"/api/tables/{tableId}/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await strangerClient.DeleteAsync($"/api/tables/{tableId}/permanent"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await IdsAsync(ownerClient, "/api/tables/trash")).Should().Contain(tableId, "the owner's trash is untouched");
    }

    [Fact]
    public async Task Purging_a_table_that_is_not_in_the_trash_is_404()
    {
        var user = NewUser();
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        var tableId = await CreateTableAsync(client, "Top-1");

        (await client.DeleteAsync($"/api/tables/{tableId}/permanent"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IdsAsync(client, "/api/tables")).Should().Contain(tableId, "a live table is never purged");
    }

    [Fact]
    public async Task Purging_a_table_linked_into_a_paper_succeeds_and_leaves_the_paper_alone()
    {
        var user = NewUser();
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        var doc = await SeedDocumentAsync(user);
        var tableId = await CreateTableAsync(client, "Top-1");
        var block = await SeedBlockAsync(doc.Id, "table",
            """{"headers":["Model","Top-1"],"rows":[["ResNet","76.1"]],"caption":"Top-1","label":"tab:trash"}""");
        (await client.PostAsync($"/api/tables/{tableId}/documents/{doc.Id}?blockId={block.Id}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await client.DeleteAsync($"/api/tables/{tableId}");

        var trashed = (await JsonOf(await client.GetAsync("/api/tables/trash"))).EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == tableId);
        trashed.GetProperty("documentCount").GetInt32().Should().Be(1, "the paper still links it");

        (await client.DeleteAsync($"/api/tables/{tableId}/permanent"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        (await db.Tables.IgnoreQueryFilters().AnyAsync(t => t.Id == tableId)).Should().BeFalse();
        (await db.DocumentTables.IgnoreQueryFilters().AnyAsync(dt => dt.TableId == tableId)).Should().BeFalse();
        (await db.Blocks.SingleAsync(b => b.Id == block.Id)).Content.RootElement
            .GetProperty("rows")[0][1].GetString().Should().Be("76.1", "the paper keeps its own content");
    }
}
