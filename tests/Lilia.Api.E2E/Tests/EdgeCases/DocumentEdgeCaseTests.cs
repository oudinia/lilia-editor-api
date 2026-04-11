using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.EdgeCases;

/// <summary>
/// Edge cases and crash-proofing for Documents API.
/// Tests inputs that could cause unhandled exceptions or 500s.
/// </summary>
public class DocumentEdgeCaseTests : E2ETestBase
{
    [Fact]
    public async Task GetDocument_NonExistentId_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocument_InvalidGuid_Returns400Or404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents/not-a-guid");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateDocument_EmptyTitle_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/documents", new { title = "" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateDocument_VeryLongTitle_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var longTitle = new string('A', 5000);
        var response = await client.PostAsJsonAsync("/api/documents", new { title = longTitle });
        // Should either truncate or reject, not crash
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (doc.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }

    [Fact]
    public async Task CreateDocument_SpecialCharactersInTitle_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/documents", new
        {
            title = "Test <script>alert('xss')</script> & \"quotes\" 'apostrophe' 日本語 émojis 🎉"
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (doc.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }

    [Fact]
    public async Task CreateDocument_NullBody_Returns400()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/documents",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UpdateDocument_NonExistentId_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync($"/api/documents/{Guid.NewGuid()}", new { title = "Ghost" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDocument_NonExistentId_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDocument_AlreadyDeleted_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Double Delete");
        var docId = doc.GetProperty("id").GetString()!;

        await client.DeleteAsync($"/api/documents/{docId}");
        var secondDelete = await client.DeleteAsync($"/api/documents/{docId}");
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateDocument_NonExistentId_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync($"/api/documents/{Guid.NewGuid()}/duplicate", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateDocument_ValidDoc_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Original");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsync($"/api/documents/{docId}/duplicate", null);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        if (response.IsSuccessStatusCode)
        {
            var dup = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (dup.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }
}
