using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// Cross-entity operations — operations on entities from different documents,
/// orphaned references, wrong document IDs.
/// </summary>
public class CrossEntityTests : E2ETestBase
{
    [Fact]
    public async Task GetBlock_FromWrongDocument_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc1 = await CreateTestDocumentAsync(client, "Doc A");
        var doc2 = await CreateTestDocumentAsync(client, "Doc B");
        var docId1 = doc1.GetProperty("id").GetString()!;
        var docId2 = doc2.GetProperty("id").GetString()!;

        var blockResp = await client.PostAsJsonAsync($"/api/documents/{docId1}/blocks", new
            { type = "paragraph", content = new { text = "Block in Doc A" } });
        var block = await blockResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        // Try to get block from wrong document
        var resp = await client.GetAsync($"/api/documents/{docId2}/blocks/{blockId}");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteBlock_FromWrongDocument_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc1 = await CreateTestDocumentAsync(client, "Doc C");
        var doc2 = await CreateTestDocumentAsync(client, "Doc D");
        var docId1 = doc1.GetProperty("id").GetString()!;
        var docId2 = doc2.GetProperty("id").GetString()!;

        var blockResp = await client.PostAsJsonAsync($"/api/documents/{docId1}/blocks", new
            { type = "paragraph", content = new { text = "Wrong doc" } });
        var block = await blockResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var resp = await client.DeleteAsync($"/api/documents/{docId2}/blocks/{blockId}");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Comment_OnNonExistentBlock_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var resp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new
        {
            content = "Comment on ghost block",
            blockId = Guid.NewGuid().ToString(),
        });
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task BibEntry_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/bibliography", new
        {
            citeKey = "orphan2024",
            entryType = "article",
            data = new { title = "Orphan", author = "Test", year = "2024" },
        });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveLabel_NotAssigned_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var labelResp = await client.PostAsJsonAsync("/api/labels", new { name = "Unassigned", color = "#ABCDEF" });
        if (!labelResp.IsSuccessStatusCode) return;
        var label = await labelResp.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetString()!;
        TrackForCleanup("/api/labels", labelId);

        // Remove label that was never assigned
        var resp = await client.DeleteAsync($"/api/documents/{docId}/labels/{labelId}");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Version_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/versions", new { });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preview_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync($"/api/documents/{Guid.NewGuid()}/preview/html/full");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sections_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync($"/api/documents/{Guid.NewGuid()}/preview/sections");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Comments_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync($"/api/documents/{Guid.NewGuid()}/comments");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Versions_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync($"/api/documents/{Guid.NewGuid()}/versions");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StudioTree_OnNonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync($"/api/studio/{Guid.NewGuid()}/tree");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidateBlock_NonExistentBlock_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsync($"/api/latex/block/{Guid.NewGuid()}/validate", null);
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
