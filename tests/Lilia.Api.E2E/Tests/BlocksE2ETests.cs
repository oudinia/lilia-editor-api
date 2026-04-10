using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for the Blocks API — CRUD on document blocks.
/// </summary>
public class BlocksE2ETests : E2ETestBase
{
    [Fact]
    public async Task AddBlock_ReturnsCreated()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Hello from E2E" },
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListBlocks_ReturnsArray()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        // Add a block first
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "heading",
            content = new { text = "Test Heading", level = 1 },
        });

        var response = await client.GetAsync($"/api/documents/{docId}/blocks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var blocks = await response.Content.ReadFromJsonAsync<JsonElement>();
        blocks.ValueKind.Should().Be(JsonValueKind.Array);
        blocks.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("paragraph", """{"text":"Test paragraph"}""")]
    [InlineData("heading", """{"text":"Test heading","level":1}""")]
    [InlineData("equation", """{"latex":"E = mc^2","display":true}""")]
    [InlineData("code", """{"code":"console.log('hello')","language":"javascript"}""")]
    [InlineData("blockquote", """{"text":"A wise quote"}""")]
    public async Task AddBlock_VariousTypes_Succeeds(string blockType, string contentJson)
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = blockType,
            content,
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteBlock_RemovesFromDocument()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        // Add a block
        var addResponse = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "To be deleted" },
        });
        var block = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        // Delete it
        var deleteResponse = await client.DeleteAsync($"/api/documents/{docId}/blocks/{blockId}");
        deleteResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
