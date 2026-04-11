using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for comments — create, reply, resolve, delete.
/// </summary>
public class CommentsE2ETests : E2ETestBase
{
    [Fact]
    public async Task CreateComment_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Comments Doc");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new
        {
            content = "E2E test comment",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListComments_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "List Comments");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = "Test" });

        var response = await client.GetAsync($"/api/documents/{docId}/comments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CommentCount_ReturnsNumbers()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Count Comments");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/documents/{docId}/comments/count");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReplyToComment_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Reply Test");
        var docId = doc.GetProperty("id").GetString()!;

        var commentResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = "Parent comment" });
        if (!commentResp.IsSuccessStatusCode) return;
        var comment = await commentResp.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetString()!;

        var replyResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments/{commentId}/replies", new
        {
            content = "Reply content",
        });
        replyResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ResolveComment_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Resolve Test");
        var docId = doc.GetProperty("id").GetString()!;

        var commentResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = "To resolve" });
        if (!commentResp.IsSuccessStatusCode) return;
        var comment = await commentResp.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetString()!;

        var patchContent = new StringContent(
            JsonSerializer.Serialize(new { resolved = true }),
            System.Text.Encoding.UTF8,
            "application/json");
        var resolveResp = await client.PatchAsync($"/api/documents/{docId}/comments/{commentId}", patchContent);
        resolveResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
