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
        var blocks = await response.Content.ReadFromJsonAsync<List<BlockDto>>();
        blocks.Should().HaveCount(2);
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
}
