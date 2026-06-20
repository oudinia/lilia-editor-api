using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class BlocksControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string OtherUserId = "test_user_002";

    public BlocksControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<(Lilia.Core.Entities.Document Doc, Lilia.Core.Entities.User User)> SeedDocWithOwner()
    {
        var user = await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        var doc = await SeedDocumentAsync(UserId, "Test Doc");
        return (doc, user);
    }

    // --- GET blocks ---

    [Fact]
    public async Task GetBlocks_ReturnsBlocksForDocument()
    {
        var (doc, _) = await SeedDocWithOwner();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello"}""", 0);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Title","level":1}""", 1);

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/blocks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blocks = await response.Content.ReadFromJsonAsync<List<BlockDto>>();
        blocks.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBlocks_Returns403_WhenNotOwner()
    {
        await SeedUserAsync(UserId);
        await SeedUserAsync(OtherUserId);
        var doc = await SeedDocumentAsync(OtherUserId, "Other's Doc");

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/blocks");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBlocks_Returns401_WhenAnonymous()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId);

        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync($"/api/documents/{doc.Id}/blocks");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- POST block ---

    [Fact]
    public async Task CreateBlock_Paragraph_ReturnsCreated()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "paragraph",
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"New paragraph"}"""),
            sortOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("paragraph");
        block.DocumentId.Should().Be(doc.Id);
    }

    [Fact]
    public async Task CreateBlock_Heading_ReturnsCreated()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "heading",
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"My Heading","level":1}"""),
            sortOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("heading");
    }

    // --- PUT block ---

    [Fact]
    public async Task UpdateBlock_UpdatesContent()
    {
        var (doc, _) = await SeedDocWithOwner();
        var seeded = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Old"}""");

        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}/blocks/{seeded.Id}", new
        {
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"Updated"}""")
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Content.GetProperty("text").GetString().Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateBlock_Returns404_WhenBlockDoesNotExist()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}/blocks/{Guid.NewGuid()}", new
        {
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"Nothing"}""")
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- DELETE block ---

    [Fact]
    public async Task DeleteBlock_Returns204_AndBlockIsGone()
    {
        var (doc, _) = await SeedDocWithOwner();
        var seeded = await SeedBlockAsync(doc.Id);

        var deleteResponse = await Client.DeleteAsync($"/api/documents/{doc.Id}/blocks/{seeded.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/documents/{doc.Id}/blocks/{seeded.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- POST footnote block ---

    [Fact]
    public async Task CreateBlock_Footnote_ReturnsCreated()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "footnote",
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"This is a footnote.","number":1}"""),
            sortOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("footnote");
        block.Content.GetProperty("text").GetString().Should().Be("This is a footnote.");
    }

    // --- POST embed block ---

    [Fact]
    public async Task CreateBlock_Embed_LaTeX_ReturnsCreated()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "embed",
            content = JsonSerializer.Deserialize<JsonElement>("""{"engine":"latex","code":"\\begin{tikzpicture}\\draw (0,0) circle (1);\\end{tikzpicture}","caption":"A circle","label":"fig:circle"}"""),
            sortOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("embed");
        block.Content.GetProperty("engine").GetString().Should().Be("latex");
        block.Content.GetProperty("code").GetString().Should().Contain("tikzpicture");
    }

    [Fact]
    public async Task CreateBlock_Embed_Typst_ReturnsCreated()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "embed",
            content = JsonSerializer.Deserialize<JsonElement>("""{"engine":"typst","code":"#circle(radius: 1cm)"}"""),
            sortOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("embed");
        block.Content.GetProperty("engine").GetString().Should().Be("typst");
    }

    // --- POST columnBreak block ---

    [Fact]
    public async Task CreateBlock_ColumnBreak_ReturnsCreated()
    {
        var (doc, _) = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "columnBreak",
            content = JsonSerializer.Deserialize<JsonElement>("""{}"""),
            sortOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("columnBreak");
    }

    // --- Footnote ordering ---

    [Fact]
    public async Task GetBlocks_FootnotesPreserveOrder()
    {
        var (doc, _) = await SeedDocWithOwner();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Main text"}""", 0);
        await SeedBlockAsync(doc.Id, "footnote", """{"text":"First footnote","number":1}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"More text"}""", 2);
        await SeedBlockAsync(doc.Id, "footnote", """{"text":"Second footnote","number":2}""", 3);

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/blocks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blocks = await response.Content.ReadFromJsonAsync<List<BlockDto>>();
        blocks.Should().HaveCount(4);
        blocks![1].Type.Should().Be("footnote");
        blocks[3].Type.Should().Be("footnote");
    }

    // --- PUT convert ---

    [Fact]
    public async Task ConvertBlock_ParagraphToHeading_Succeeds()
    {
        var (doc, _) = await SeedDocWithOwner();
        var seeded = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Convert me"}""");

        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}/blocks/{seeded.Id}/convert", new
        {
            newType = "heading"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var block = await response.Content.ReadFromJsonAsync<BlockDto>();
        block!.Type.Should().Be("heading");
    }

    [Fact]
    public async Task ConvertBlock_InvalidType_Returns400()
    {
        var (doc, _) = await SeedDocWithOwner();
        var seeded = await SeedBlockAsync(doc.Id, "paragraph");

        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}/blocks/{seeded.Id}/convert", new
        {
            newType = "nonexistent_type"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- POST batch ---

    [Fact]
    public async Task BatchUpdateBlocks_UpdatesMultipleBlocks()
    {
        var (doc, _) = await SeedDocWithOwner();
        var block1 = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"One"}""", 0);
        var block2 = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Two"}""", 1);

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks/batch", new
        {
            blocks = new[]
            {
                new { id = block1.Id, content = JsonSerializer.Deserialize<JsonElement>("""{"text":"Updated One"}""") },
                new { id = block2.Id, content = JsonSerializer.Deserialize<JsonElement>("""{"text":"Updated Two"}""") }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The batch endpoint returns a BatchUpdateResultDto envelope
        // (blocks + document version for optimistic concurrency), not a
        // bare block array.
        var result = await response.Content.ReadFromJsonAsync<BatchUpdateResultDto>();
        result.Should().NotBeNull();
        result!.Blocks.Should().HaveCount(2);
    }

    // --- PUT reorder ---

    [Fact]
    public async Task ReorderBlocks_ReordersBlockIds()
    {
        var (doc, _) = await SeedDocWithOwner();
        var block1 = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"First"}""", 0);
        var block2 = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Second"}""", 1);

        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}/blocks/reorder", new
        {
            blockIds = new[] { block2.Id, block1.Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blocks = await response.Content.ReadFromJsonAsync<List<BlockDto>>();
        blocks![0].Id.Should().Be(block2.Id);
        blocks[1].Id.Should().Be(block1.Id);
    }

    // --- LILIA-146: insert-at-position shifts existing rows ---

    [Fact]
    public async Task CreateBlock_AtCollidingSortOrder_ShiftsExistingRowsUp()
    {
        var (doc, _) = await SeedDocWithOwner();
        // Three pre-existing blocks at sort_order 0, 1, 2.
        var existingA = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"A"}""", 0);
        var existingB = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"B"}""", 1);
        var existingC = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"C"}""", 2);

        // Insert at sort_order=1 (collides with B). Pre-LILIA-146 this
        // landed at 1 alongside B with unstable visual ordering; now
        // B/C shift up to 2/3 and the new block lands cleanly at 1.
        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "paragraph",
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"NEW"}"""),
            sortOrder = 1,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Re-list and verify the order: A(0), NEW(1), B(2), C(3).
        var listResp = await Client.GetAsync($"/api/documents/{doc.Id}/blocks");
        var blocks = await listResp.Content.ReadFromJsonAsync<List<BlockDto>>();
        blocks.Should().HaveCount(4);
        blocks!.Select(b => b.SortOrder).Should().BeEquivalentTo(new[] { 0, 1, 2, 3 }, o => o.WithStrictOrdering());
        blocks[0].Id.Should().Be(existingA.Id);
        blocks[1].Type.Should().Be("paragraph"); // the new block
        blocks[2].Id.Should().Be(existingB.Id);
        blocks[3].Id.Should().Be(existingC.Id);
    }

    [Fact]
    public async Task CreateBlock_AtNonCollidingSortOrder_DoesNotShift()
    {
        var (doc, _) = await SeedDocWithOwner();
        // Hole between sort_order 0 and 5.
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"A"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"B"}""", 5);

        // Insert at sort_order=2 — nothing exists at 2 or above except
        // the row at 5, which still gets shifted to 6 because the WHERE
        // is `>= insertAt`. Acceptable trade-off: a "hole" insert
        // pushes anything above; the alternative (smart hole-fill) is
        // more code for a marginal case.
        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks", new
        {
            type = "paragraph",
            content = JsonSerializer.Deserialize<JsonElement>("""{"text":"NEW"}"""),
            sortOrder = 2,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResp = await Client.GetAsync($"/api/documents/{doc.Id}/blocks");
        var blocks = await listResp.Content.ReadFromJsonAsync<List<BlockDto>>();
        blocks!.Select(b => b.SortOrder).Should().BeEquivalentTo(new[] { 0, 2, 6 }, o => o.WithStrictOrdering());
    }
}
