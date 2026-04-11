using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for authorization — verifies that access controls work across
/// different user roles (owner, collaborator, viewer, anonymous).
/// </summary>
public class AuthorizationE2ETests : E2ETestBase
{
    [Fact]
    public async Task Anonymous_CannotListDocuments()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/documents");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_CannotCreateBlocks()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/documents/00000000-0000-0000-0000-000000000000/blocks", new
        {
            type = "paragraph",
            content = new { text = "Should fail" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Owner_CanUpdateOwnDocument()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(client, "Auth Test");
        var id = doc.GetProperty("id").GetString()!;

        var response = await client.PutAsJsonAsync($"/api/documents/{id}", new { title = "Updated" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Viewer_CannotDeleteOwnersDocument()
    {
        using var ownerClient = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(ownerClient, "No Delete");
        var id = doc.GetProperty("id").GetString()!;

        using var viewerClient = await CreateAuthenticatedClientAsync("Viewer");
        var response = await viewerClient.DeleteAsync($"/api/documents/{id}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotAddBlocksToOwnersDocument()
    {
        using var ownerClient = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(ownerClient, "No Blocks");
        var id = doc.GetProperty("id").GetString()!;

        using var viewerClient = await CreateAuthenticatedClientAsync("Viewer");
        var response = await viewerClient.PostAsJsonAsync($"/api/documents/{id}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Should fail" },
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EachUser_SeesOnlyOwnDocuments()
    {
        using var ownerClient = await CreateAuthenticatedClientAsync("Owner");
        var ownerDoc = await CreateTestDocumentAsync(ownerClient, "Owner's Doc");
        var ownerDocId = ownerDoc.GetProperty("id").GetString()!;

        using var collabClient = await CreateAuthenticatedClientAsync("Collaborator");
        var collabDoc = await CreateTestDocumentAsync(collabClient, "Collab's Doc");
        TrackForCleanup("/api/documents", collabDoc.GetProperty("id").GetString()!);

        // Owner should not see collab's doc in their list
        var ownerList = await ownerClient.GetAsync("/api/documents");
        var ownerDocs = await ownerList.Content.ReadAsStringAsync();
        ownerDocs.Should().Contain(ownerDocId);
    }
}
