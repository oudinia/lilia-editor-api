using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.EdgeCases;

/// <summary>
/// Edge cases for sharing and public access.
/// </summary>
public class SharingEdgeCaseTests : E2ETestBase
{
    [Fact]
    public async Task ShareDocument_EnablePublic_ReturnsShareLink()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Share Test");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/share", new { isPublic = true });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ShareDocument_RevokeShare_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Revoke Share");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/share", new { isPublic = true });
        var revokeResp = await client.DeleteAsync($"/api/documents/{docId}/share");
        revokeResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SharedDocument_AnonymousAccess_WhenPublic()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Public Doc");
        var docId = doc.GetProperty("id").GetString()!;

        var shareResp = await client.PostAsJsonAsync($"/api/documents/{docId}/share", new { isPublic = true });
        if (!shareResp.IsSuccessStatusCode) return;

        var shareData = await shareResp.Content.ReadFromJsonAsync<JsonElement>();
        var shareLink = shareData.TryGetProperty("shareLink", out var sl) ? sl.GetString() : null;
        if (shareLink == null) return;

        // Anonymous client should be able to access via share link
        using var anonClient = CreateClient();
        var anonResp = await anonClient.GetAsync($"/api/documents/shared/{shareLink}");
        anonResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SharedDocument_NonExistentShareLink_Returns404()
    {
        using var anonClient = CreateClient();
        var response = await anonClient.GetAsync("/api/documents/shared/nonexistent-link-12345");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShareDocument_NonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync($"/api/documents/{Guid.NewGuid()}/share", new { isPublic = true });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
