using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for Draft Blocks API — CRUD, favorites, commit to document.
/// </summary>
public class DraftBlocksE2ETests : E2ETestBase
{
    [Fact]
    public async Task ListDraftBlocks_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/draft-blocks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCategories_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/draft-blocks/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateDraftBlock_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/draft-blocks", new
        {
            type = "paragraph",
            content = new { text = "Draft paragraph" },
            title = "E2E Draft",
            category = "general",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (response.IsSuccessStatusCode)
        {
            var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (draft.TryGetProperty("id", out var id))
                TrackForCleanup("/api/draft-blocks", id.GetString()!);
        }
    }

    [Fact]
    public async Task CommitDraftToDocument_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Draft Target");
        var docId = doc.GetProperty("id").GetString()!;

        // Create draft
        var draftResp = await client.PostAsJsonAsync("/api/draft-blocks", new
        {
            type = "heading",
            content = new { text = "Draft Heading", level = 2 },
            title = "E2E Commit Draft",
            category = "general",
        });
        if (!draftResp.IsSuccessStatusCode) return;
        var draft = await draftResp.Content.ReadFromJsonAsync<JsonElement>();
        var draftId = draft.GetProperty("id").GetString()!;

        // Commit to document
        var commitResp = await client.PostAsJsonAsync($"/api/draft-blocks/{draftId}/commit", new
        {
            documentId = docId,
        });
        commitResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateDraftFromBlock_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Draft Source");
        var docId = doc.GetProperty("id").GetString()!;

        // Create a real block
        var blockResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Source block" },
        });
        if (!blockResp.IsSuccessStatusCode) return;
        var block = await blockResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        // Create draft from it
        var draftResp = await client.PostAsJsonAsync("/api/draft-blocks/from-block", new
        {
            blockId,
            documentId = docId,
        });
        draftResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (draftResp.IsSuccessStatusCode)
        {
            var draft = await draftResp.Content.ReadFromJsonAsync<JsonElement>();
            if (draft.TryGetProperty("id", out var id))
                TrackForCleanup("/api/draft-blocks", id.GetString()!);
        }
    }
}
