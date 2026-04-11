using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.EdgeCases;

/// <summary>
/// Concurrency and race condition tests — rapid operations, parallel requests.
/// </summary>
public class ConcurrencyEdgeCaseTests : E2ETestBase
{
    [Fact]
    public async Task RapidBlockCreation_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Rapid Blocks");
        var docId = doc.GetProperty("id").GetString()!;

        // Create 10 blocks rapidly in sequence
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            {
                type = "paragraph",
                content = new { text = $"Block {i}" },
            });
            response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError,
                $"block {i} should not cause server error");
        }
    }

    [Fact]
    public async Task ParallelBlockCreation_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Parallel Blocks");
        var docId = doc.GetProperty("id").GetString()!;

        // Create 5 blocks in parallel
        var tasks = Enumerable.Range(0, 5).Select(i =>
            client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            {
                type = "paragraph",
                content = new { text = $"Parallel block {i}" },
            })
        ).ToArray();

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }
    }

    [Fact]
    public async Task CreateAndImmediatelyDelete_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Quick Delete");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Ephemeral" },
        });
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var deleteResp = await client.DeleteAsync($"/api/documents/{docId}/blocks/{blockId}");
        deleteResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Delete again should be 404
        var deleteAgain = await client.DeleteAsync($"/api/documents/{docId}/blocks/{blockId}");
        deleteAgain.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RapidVersionCreation_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Rapid Versions");
        var docId = doc.GetProperty("id").GetString()!;

        // Create 3 versions rapidly
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
            response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }
    }

    [Fact]
    public async Task UpdateDocumentWhileExporting_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Update During Export");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Content" },
        });

        // Start export and update concurrently
        var exportTask = client.GetAsync($"/api/documents/{docId}/export/latex");
        var updateTask = client.PutAsJsonAsync($"/api/documents/{docId}", new { title = "Updated Title" });

        var results = await Task.WhenAll(exportTask, updateTask);
        foreach (var r in results)
        {
            r.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }
    }
}
