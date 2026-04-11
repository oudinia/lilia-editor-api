using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Block type conversion combinations — paragraph↔heading, etc.
/// </summary>
public class BlockConversionTests : E2ETestBase
{
    [Theory]
    [InlineData("paragraph", "heading")]
    [InlineData("paragraph", "blockquote")]
    [InlineData("paragraph", "code")]
    [InlineData("heading", "paragraph")]
    [InlineData("blockquote", "paragraph")]
    [InlineData("code", "paragraph")]
    [InlineData("paragraph", "equation")]
    [InlineData("paragraph", "list")]
    public async Task ConvertBlock_BetweenTypes_DoesNotCrash(string fromType, string toType)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Convert-{fromType}-{toType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = fromType switch
        {
            "heading" => (object)new { text = "Heading text", level = 1 },
            "code" => new { code = "x = 1", language = "python" },
            "equation" => new { latex = "x^2", display = true },
            "list" => new { items = new[] { "item" } },
            "blockquote" => new { text = "A quote" },
            _ => new { text = "Paragraph text" },
        };

        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = fromType, content });
        if (!createResp.IsSuccessStatusCode) return;
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var convertResp = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}/convert", new { type = toType });
        convertResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"converting {fromType}→{toType} should not crash");
    }

    [Fact]
    public async Task ConvertBlock_ToSameType_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Convert Same");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Same type" },
        });
        if (!createResp.IsSuccessStatusCode) return;
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var convertResp = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}/convert", new { type = "paragraph" });
        convertResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ConvertBlock_ToInvalidType_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Convert Invalid");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Convert me" },
        });
        if (!createResp.IsSuccessStatusCode) return;
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var convertResp = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}/convert", new { type = "nonexistent" });
        convertResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
