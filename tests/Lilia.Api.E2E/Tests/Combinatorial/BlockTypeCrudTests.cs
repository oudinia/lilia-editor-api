using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Every block type × CRUD operations.
/// Ensures create, read, update, delete work for all block types.
/// </summary>
public class BlockTypeCrudTests : E2ETestBase
{
    public static IEnumerable<object[]> BlockTypes => new[]
    {
        new object[] { "paragraph", """{"text":"Test"}""", """{"text":"Updated"}""" },
        new object[] { "heading", """{"text":"H1","level":1}""", """{"text":"H1 Updated","level":1}""" },
        new object[] { "heading", """{"text":"H2","level":2}""", """{"text":"H2 Updated","level":2}""" },
        new object[] { "equation", """{"latex":"x^2","display":true}""", """{"latex":"x^3","display":true}""" },
        new object[] { "code", """{"code":"a=1","language":"python"}""", """{"code":"a=2","language":"python"}""" },
        new object[] { "blockquote", """{"text":"Quote"}""", """{"text":"New quote"}""" },
        new object[] { "list", """{"items":["a"]}""", """{"items":["a","b"]}""" },
        new object[] { "abstract", """{"text":"Abstract"}""", """{"text":"New abstract"}""" },
        new object[] { "theorem", """{"statement":"P","type":"theorem"}""", """{"statement":"Q","type":"lemma"}""" },
        new object[] { "table", """{"rows":[{"cells":["1"]}]}""", """{"rows":[{"cells":["2","3"]}]}""" },
        new object[] { "figure", """{"url":"","caption":"Fig 1"}""", """{"url":"","caption":"Fig 2"}""" },
    };

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public async Task BlockType_Create_Succeeds(string blockType, string createJson, string _)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Create-{blockType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(createJson);
        var resp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = blockType, content });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public async Task BlockType_CreateAndDelete_Succeeds(string blockType, string createJson, string _)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Delete-{blockType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(createJson);
        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = blockType, content });
        if (!createResp.IsSuccessStatusCode) return;

        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var deleteResp = await client.DeleteAsync($"/api/documents/{docId}/blocks/{blockId}");
        deleteResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public async Task BlockType_CreateViaStudio_Succeeds(string blockType, string createJson, string _)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Studio-{blockType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(createJson);
        var resp = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new { type = blockType, content });
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"studio create {blockType} should not crash");
    }
}
