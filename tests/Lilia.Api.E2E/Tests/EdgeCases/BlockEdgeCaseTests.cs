using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.EdgeCases;

/// <summary>
/// Edge cases for Blocks API — malformed content, invalid types, boundary conditions.
/// </summary>
public class BlockEdgeCaseTests : E2ETestBase
{
    [Fact]
    public async Task AddBlock_UnknownType_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "nonexistent_block_type",
            content = new { text = "test" },
        });
        // Should reject or fallback, not crash
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AddBlock_EmptyContent_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AddBlock_NullContent_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = (object?)null,
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AddBlock_StringContent_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        // Content as plain string instead of object — this caused DOCX export crash
        var payload = new StringContent(
            """{"type":"paragraph","content":"just a string"}""",
            System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/documents/{docId}/blocks", payload);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AddBlock_HugeContent_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var hugeText = new string('X', 100_000);
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = hugeText },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AddBlock_MaliciousLatex_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "equation",
            content = new { latex = @"\input{/etc/passwd}", display = true },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AddBlock_ToNonExistentDocument_ReturnsErrorNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/blocks", new
        {
            type = "paragraph",
            content = new { text = "orphan block" },
        });
        // May return 404 (not found) or 403 (forbidden) depending on access check order
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteBlock_NonExistent_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.DeleteAsync($"/api/documents/{docId}/blocks/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddBlock_SpecialUnicode_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "数学公式 ∫∑∏ αβγδ 🧮📐 RTL: مرحبا ZWJ: 👨‍👩‍👧‍👦" },
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("table", """{"rows":[]}""")]
    [InlineData("list", """{"items":[]}""")]
    [InlineData("figure", """{"url":"","caption":""}""")]
    [InlineData("abstract", """{"text":""}""")]
    [InlineData("theorem", """{"statement":"","type":"theorem"}""")]
    public async Task AddBlock_EmptyStructuredContent_HandledGracefully(string blockType, string contentJson)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = blockType,
            content,
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
