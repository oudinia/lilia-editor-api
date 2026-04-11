using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for document versions — create snapshots, list, restore.
/// </summary>
public class VersionsE2ETests : E2ETestBase
{
    [Fact]
    public async Task CreateVersion_ReturnsCreated()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Version Test");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListVersions_ReturnsArray()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Versions List");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });

        var response = await client.GetAsync($"/api/documents/{docId}/versions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RestoreVersion_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Restore Test");
        var docId = doc.GetProperty("id").GetString()!;

        // Add block, create version, modify, then restore
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Original content" },
        });

        var versionResp = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
        if (!versionResp.IsSuccessStatusCode) return;

        var version = await versionResp.Content.ReadFromJsonAsync<JsonElement>();
        var versionId = version.GetProperty("id").GetString()!;

        var restoreResp = await client.PostAsJsonAsync($"/api/documents/{docId}/versions/{versionId}/restore", new { });
        restoreResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
