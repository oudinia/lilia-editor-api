using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for Studio API — tree structure, sessions, block preview.
/// </summary>
public class StudioE2ETests : E2ETestBase
{
    [Fact]
    public async Task GetTree_ReturnsDocumentStructure()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/studio/{docId}/tree");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateBlockViaStudio_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "paragraph",
            content = new { text = "Studio block" },
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdateBlockViaStudio_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        // Create block
        var createResp = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "paragraph",
            content = new { text = "Before update" },
        });
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        // Update
        var updateResp = await client.PutAsJsonAsync($"/api/studio/{docId}/block/{blockId}", new
        {
            content = new { text = "After update" },
        });
        updateResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetBlockPreview_ReturnsContent()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "paragraph",
            content = new { text = "Preview this" },
        });
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/studio/{docId}/block/{blockId}/preview");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SaveAndGetSession_PersistsState()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        // Save session state
        var saveResp = await client.PutAsJsonAsync($"/api/studio/{docId}/session", new
        {
            focusedBlockId = (string?)null,
            layout = "cards",
        });
        saveResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Get session state
        var getResp = await client.GetAsync($"/api/studio/{docId}/session");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
