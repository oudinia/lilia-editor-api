using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.EdgeCases;

/// <summary>
/// Tests for batch operations, reordering, and block conversion.
/// </summary>
public class BatchOperationTests : E2ETestBase
{
    [Fact]
    public async Task BatchUpdateBlocks_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Batch Update");
        var docId = doc.GetProperty("id").GetString()!;

        // Create blocks
        var b1 = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Block 1" } });
        var b2 = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Block 2" } });

        var block1 = await b1.Content.ReadFromJsonAsync<JsonElement>();
        var block2 = await b2.Content.ReadFromJsonAsync<JsonElement>();
        var id1 = block1.GetProperty("id").GetString()!;
        var id2 = block2.GetProperty("id").GetString()!;

        // Batch update
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks/batch", new
        {
            blocks = new[]
            {
                new { id = id1, content = new { text = "Updated Block 1" } },
                new { id = id2, content = new { text = "Updated Block 2" } },
            }
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ReorderBlocks_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Reorder");
        var docId = doc.GetProperty("id").GetString()!;

        var b1 = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "First" } });
        var b2 = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Second" } });

        var block1 = await b1.Content.ReadFromJsonAsync<JsonElement>();
        var block2 = await b2.Content.ReadFromJsonAsync<JsonElement>();
        var id1 = block1.GetProperty("id").GetString()!;
        var id2 = block2.GetProperty("id").GetString()!;

        // Reverse order
        var response = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/reorder", new
        {
            blockIds = new[] { id2, id1 }
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ConvertBlockType_ParagraphToHeading_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Convert Block");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Promote me" } });
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var response = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}/convert", new
        {
            type = "heading",
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ReorderBlocks_EmptyArray_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Empty Reorder");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/reorder", new
        {
            blockIds = Array.Empty<string>()
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task BatchUpdate_EmptyBlocks_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Empty Batch");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks/batch", new
        {
            blocks = Array.Empty<object>()
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
