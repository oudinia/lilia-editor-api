using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for authorization — verifies that access controls work across
/// different user roles (owner, collaborator, viewer, anonymous).
///
/// Note: Multi-user tests (Owner vs Viewer) only work in DevJwt mode where
/// each test user gets a unique sub claim. In Kinde M2M mode, all requests
/// share the same service account identity.
/// </summary>
public class AuthorizationE2ETests : E2ETestBase
{
    private bool IsMultiUserSupported => Config.AuthMode == "DevJwt";

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
        if (!IsMultiUserSupported)
        {
            // M2M mode: all users share the same identity, skip multi-user test
            return;
        }

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
        if (!IsMultiUserSupported)
        {
            return;
        }

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
    public async Task DifferentUser_CannotAccessOtherUsersDocument()
    {
        if (!IsMultiUserSupported)
        {
            return;
        }

        using var ownerClient = await CreateAuthenticatedClientAsync("Owner");
        var created = await CreateTestDocumentAsync(ownerClient, "E2E Private Doc");
        var id = created.GetProperty("id").GetString();

        using var viewerClient = await CreateAuthenticatedClientAsync("Viewer");
        var response = await viewerClient.GetAsync($"/api/documents/{id}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }
}
