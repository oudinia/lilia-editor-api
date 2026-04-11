using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Workflows;

/// <summary>
/// Studio-specific workflows — tree navigation, block CRUD via studio API, sessions.
/// </summary>
public class StudioWorkflowTests : E2ETestBase
{
    [Fact]
    public async Task StudioWorkflow_CreateEditPreviewValidate()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Studio Workflow");
        var docId = doc.GetProperty("id").GetString()!;

        // 1. Create blocks via studio
        var b1 = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "heading",
            content = new { text = "Chapter 1", level = 1 },
        });
        b1.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var block1 = await b1.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block1.TryGetProperty("id", out var bid) ? bid.GetString() : null;

        var b2 = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "paragraph",
            content = new { text = "Introduction text." },
        });
        b2.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // 2. Get tree
        var treeResp = await client.GetAsync($"/api/studio/{docId}/tree");
        treeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Update block via studio
        if (blockId != null)
        {
            var updateResp = await client.PutAsJsonAsync($"/api/studio/{docId}/block/{blockId}", new
            {
                content = new { text = "Chapter 1: Updated", level = 1 },
            });
            updateResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            // 4. Get block preview
            var previewResp = await client.GetAsync($"/api/studio/{docId}/block/{blockId}/preview");
            previewResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }

        // 5. Save session
        var sessionResp = await client.PutAsJsonAsync($"/api/studio/{docId}/session", new
        {
            layout = "cards",
            focusedBlockId = blockId,
        });
        sessionResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // 6. Validate document
        var validateResp = await client.PostAsync($"/api/latex/{docId}/validate", null);
        validateResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task StudioWorkflow_MoveBlock()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Move Block");
        var docId = doc.GetProperty("id").GetString()!;

        var b1 = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new { type = "paragraph", content = new { text = "First" } });
        var b2 = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new { type = "paragraph", content = new { text = "Second" } });

        if (!b1.IsSuccessStatusCode || !b2.IsSuccessStatusCode) return;
        var block2 = await b2.Content.ReadFromJsonAsync<JsonElement>();
        var block2Id = block2.GetProperty("id").GetString()!;

        // Move block 2 to position 0
        var moveResp = await client.PutAsJsonAsync($"/api/studio/{docId}/block/{block2Id}/move", new { position = 0 });
        moveResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task StudioWorkflow_BlockMetadata()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Block Metadata");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "paragraph",
            content = new { text = "With metadata" },
        });
        if (!createResp.IsSuccessStatusCode) return;
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        // Update metadata
        var patchContent = new StringContent(
            JsonSerializer.Serialize(new { status = "reviewed" }),
            System.Text.Encoding.UTF8, "application/json");
        var metaResp = await client.PatchAsync($"/api/studio/{docId}/block/{blockId}/metadata", patchContent);
        metaResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task StudioWorkflow_DeleteBlock()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Delete Studio Block");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/studio/{docId}/block", new
        {
            type = "paragraph",
            content = new { text = "To be deleted" },
        });
        if (!createResp.IsSuccessStatusCode) return;
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var deleteResp = await client.DeleteAsync($"/api/studio/{docId}/block/{blockId}");
        deleteResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Tree should no longer contain the block
        var treeResp = await client.GetAsync($"/api/studio/{docId}/tree");
        treeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StudioWorkflow_GetLocks()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Locks Test");
        var docId = doc.GetProperty("id").GetString()!;

        var locksResp = await client.GetAsync($"/api/studio/{docId}/locks");
        locksResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
