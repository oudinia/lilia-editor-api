using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Library;

/// <summary>
/// "Link it — edits reach all N papers." Editing a linked table has to change
/// the block in each paper that links it; a copy is its own table and does not.
/// Until now the update wrote the table row only, so the promise was false.
/// </summary>
public class LinkedTableEditTests : IntegrationTestBase
{
    private const string Author = "test_user_tables_link";

    public LinkedTableEditTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static object Body(string caption, string cell) => new
    {
        caption,
        label = "tab:top1",
        content = new { headers = new[] { "Model", "Top-1" }, rows = new[] { new[] { "ResNet", cell } } },
    };

    private async Task<Guid> CreateTableAsync(HttpClient client, string caption, string cell)
    {
        var res = await client.PostAsJsonAsync("/api/tables", Body(caption, cell));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Editing_a_linked_table_changes_the_block_in_the_paper_and_moves_its_version()
    {
        await SeedUserAsync(Author);
        using var client = CreateClientAs(Author);
        var doc = await SeedDocumentAsync(Author);
        var tableId = await CreateTableAsync(client, "Top-1", "76.1");
        var block = await SeedBlockAsync(doc.Id, "table",
            """{"headers":["Model","Top-1"],"rows":[["ResNet","76.1"]],"caption":"Top-1","label":"tab:top1","columnWidth":["3cm",""]}""");
        (await client.PostAsync($"/api/tables/{tableId}/documents/{doc.Id}?blockId={block.Id}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        int versionBefore;
        await using (var db = CreateDbContext())
            versionBefore = (await db.Documents.SingleAsync(d => d.Id == doc.Id)).Version;

        (await client.PutAsJsonAsync($"/api/tables/{tableId}", Body("Top-1 accuracy", "76.3")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var after = CreateDbContext();
        var content = (await after.Blocks.SingleAsync(b => b.Id == block.Id)).Content.RootElement;
        content.GetProperty("rows")[0][1].GetString().Should().Be("76.3");
        content.GetProperty("caption").GetString().Should().Be("Top-1 accuracy");
        content.GetProperty("columnWidth")[0].GetString().Should().Be("3cm", "the paper's own keys are kept");
        (await after.Documents.SingleAsync(d => d.Id == doc.Id)).Version
            .Should().BeGreaterThan(versionBefore, "an open editor must see the write, not overwrite it");
    }

    /// <summary>The editor's "Open in the table tool" (8 Nov 2026): which saved table a block is linked to.</summary>
    [Fact]
    public async Task A_blocks_linked_table_is_found_by_its_block_and_only_by_people_who_can_open_it()
    {
        await SeedUserAsync(Author);
        await SeedUserAsync("test_user_tables_link_other");
        using var client = CreateClientAs(Author);
        var doc = await SeedDocumentAsync(Author);
        var tableId = await CreateTableAsync(client, "Top-1", "76.1");
        var block = await SeedBlockAsync(doc.Id, "table",
            """{"headers":["Model","Top-1"],"rows":[["ResNet","76.1"]],"caption":"Top-1","label":"tab:top1"}""");
        var unlinked = await SeedBlockAsync(doc.Id, "table", """{"headers":["A"],"rows":[["1"]]}""");

        (await client.GetAsync($"/api/tables/by-block/{block.Id}?documentId={doc.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "not linked yet");
        await client.PostAsync($"/api/tables/{tableId}/documents/{doc.Id}?blockId={block.Id}", null);

        var found = await client.GetAsync($"/api/tables/by-block/{block.Id}?documentId={doc.Id}");
        found.StatusCode.Should().Be(HttpStatusCode.OK);
        (await found.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().Should().Be(tableId);

        (await client.GetAsync($"/api/tables/by-block/{unlinked.Id}?documentId={doc.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/tables/by-block/{block.Id}?documentId={Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the block must be in the document named");
        using var stranger = CreateClientAs("test_user_tables_link_other");
        (await stranger.GetAsync($"/api/tables/by-block/{block.Id}?documentId={doc.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "someone else's table is not theirs to open");
    }

    [Fact]
    public async Task A_copy_is_its_own_table_and_editing_the_original_leaves_it_alone()
    {
        await SeedUserAsync(Author);
        using var client = CreateClientAs(Author);
        var doc = await SeedDocumentAsync(Author);
        var original = await CreateTableAsync(client, "Top-1", "76.1");
        var copy = await CreateTableAsync(client, "Top-1", "76.1");
        var block = await SeedBlockAsync(doc.Id, "table",
            """{"headers":["Model","Top-1"],"rows":[["ResNet","76.1"]],"caption":"Top-1","label":"tab:top1"}""");
        await client.PostAsync($"/api/tables/{copy}/documents/{doc.Id}?blockId={block.Id}", null);

        (await client.PutAsJsonAsync($"/api/tables/{original}", Body("Top-1", "99.9")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        (await db.Blocks.SingleAsync(b => b.Id == block.Id)).Content.RootElement
            .GetProperty("rows")[0][1].GetString().Should().Be("76.1");
    }

    [Fact]
    public async Task A_link_naming_a_block_in_another_document_does_not_write_into_it()
    {
        await SeedUserAsync(Author);
        using var client = CreateClientAs(Author);
        var linked = await SeedDocumentAsync(Author);
        var other = await SeedDocumentAsync(Author);
        var tableId = await CreateTableAsync(client, "Top-1", "76.1");
        var elsewhere = await SeedBlockAsync(other.Id, "table",
            """{"headers":["A"],"rows":[["untouched"]]}""");
        // The link says `linked`, but the block id is one from `other`.
        await client.PostAsync($"/api/tables/{tableId}/documents/{linked.Id}?blockId={elsewhere.Id}", null);

        (await client.PutAsJsonAsync($"/api/tables/{tableId}", Body("Top-1", "76.3")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        (await db.Blocks.SingleAsync(b => b.Id == elsewhere.Id)).Content.RootElement
            .GetProperty("rows")[0][0].GetString().Should().Be("untouched");
    }
}
