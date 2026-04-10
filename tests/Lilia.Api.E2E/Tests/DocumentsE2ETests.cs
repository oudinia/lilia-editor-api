using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for the Documents API — CRUD, listing, and authorization.
/// </summary>
public class DocumentsE2ETests : E2ETestBase
{
    [Fact]
    public async Task CreateDocument_ReturnsCreated()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var doc = await CreateTestDocumentAsync(client, "E2E Create Test");

        doc.TryGetProperty("id", out _).Should().BeTrue("response should contain document id");
    }

    [Fact]
    public async Task ListDocuments_ReturnsOwnerDocs()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        await CreateTestDocumentAsync(client, "E2E List Test");

        var response = await client.GetAsync("/api/documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var docs = await response.Content.ReadFromJsonAsync<JsonElement>();
        docs.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetDocument_ReturnsDocument()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var created = await CreateTestDocumentAsync(client, "E2E Get Test");
        var id = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/api/documents/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("title").GetString().Should().Be("E2E Get Test");
    }

    [Fact]
    public async Task UpdateDocument_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var created = await CreateTestDocumentAsync(client, "E2E Update Before");
        var id = created.GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/documents/{id}", new { title = "E2E Update After" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteDocument_ReturnsOkOrNoContent()
    {
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var created = await CreateTestDocumentAsync(client, "E2E Delete Test");
        var id = created.GetProperty("id").GetString();

        var response = await client.DeleteAsync($"/api/documents/{id}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await client.GetAsync($"/api/documents/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnonymousUser_CannotCreateDocument()
    {
        using var client = CreateClient(); // No auth
        var response = await client.PostAsJsonAsync("/api/documents", new { title = "Should Fail" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DifferentUser_CannotAccessOtherUsersDocument()
    {
        using var ownerClient = await CreateAuthenticatedClientAsync("Owner");
        var created = await CreateTestDocumentAsync(ownerClient, "E2E Private Doc");
        var id = created.GetProperty("id").GetString();

        using var viewerClient = await CreateAuthenticatedClientAsync("Viewer");
        var response = await viewerClient.GetAsync($"/api/documents/{id}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }
}
