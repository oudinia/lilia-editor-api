using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for Snippets API — CRUD, favorites, categories.
/// </summary>
public class SnippetsE2ETests : E2ETestBase
{
    [Fact]
    public async Task ListSnippets_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/snippets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCategories_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/snippets/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateSnippet_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/snippets", new
        {
            name = "E2E Snippet",
            latexContent = @"\begin{theorem} E2E test \end{theorem}",
            blockType = "theorem",
            category = "math",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (response.IsSuccessStatusCode)
        {
            var snippet = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (snippet.TryGetProperty("id", out var id))
                TrackForCleanup("/api/snippets", id.GetString()!);
        }
    }

    [Fact]
    public async Task ToggleFavorite_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/snippets", new
        {
            name = "E2E Fav Snippet",
            latexContent = @"\int_0^1 x\,dx",
            blockType = "equation",
            category = "calculus",
        });
        if (!createResp.IsSuccessStatusCode) return;
        var snippet = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var snippetId = snippet.GetProperty("id").GetString()!;
        TrackForCleanup("/api/snippets", snippetId);

        var favResp = await client.PostAsync($"/api/snippets/{snippetId}/favorite", null);
        favResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
