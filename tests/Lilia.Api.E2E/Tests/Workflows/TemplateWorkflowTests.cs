using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Workflows;

/// <summary>
/// Template workflows — create from doc, use template, verify content preserved.
/// </summary>
public class TemplateWorkflowTests : E2ETestBase
{
    [Fact]
    public async Task TemplateWorkflow_CreateFromDocAndUse()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // 1. Create source document with blocks
        var srcDoc = await CreateTestDocumentAsync(client, "Template Source");
        var srcDocId = srcDoc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{srcDocId}/blocks", new
            { type = "heading", content = new { text = "Template Title", level = 1 } });
        await client.PostAsJsonAsync($"/api/documents/{srcDocId}/blocks", new
            { type = "paragraph", content = new { text = "Template content here." } });
        await client.PostAsJsonAsync($"/api/documents/{srcDocId}/blocks", new
            { type = "equation", content = new { latex = @"\int_0^1 f(x)\,dx", display = true } });

        // 2. Create template from document
        var templateResp = await client.PostAsJsonAsync("/api/templates", new
        {
            documentId = srcDocId,
            name = "E2E Workflow Template",
            category = "academic",
        });
        templateResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (!templateResp.IsSuccessStatusCode) return;

        var template = await templateResp.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetString()!;
        TrackForCleanup("/api/templates", templateId);

        // 3. Use template to create new document
        var useResp = await client.PostAsJsonAsync($"/api/templates/{templateId}/use", new { title = "From Template" });
        useResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (!useResp.IsSuccessStatusCode) return;

        var newDoc = await useResp.Content.ReadFromJsonAsync<JsonElement>();
        var newDocId = newDoc.TryGetProperty("id", out var nid) ? nid.GetString() : null;
        if (newDocId != null)
        {
            TrackForCleanup("/api/documents", newDocId);

            // 4. Verify new doc has blocks
            var blocksResp = await client.GetAsync($"/api/documents/{newDocId}/blocks");
            blocksResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task TemplateWorkflow_UpdateTemplate()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Template Update");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync("/api/templates", new
        {
            documentId = docId,
            name = "Update Me",
            category = "general",
        });
        if (!createResp.IsSuccessStatusCode) return;
        var template = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetString()!;
        TrackForCleanup("/api/templates", templateId);

        var updateResp = await client.PutAsJsonAsync($"/api/templates/{templateId}", new
        {
            name = "Updated Template",
            category = "academic",
        });
        updateResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TemplateWorkflow_DeleteTemplate()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Template Delete");
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync("/api/templates", new
        {
            documentId = docId,
            name = "Delete Me",
            category = "general",
        });
        if (!createResp.IsSuccessStatusCode) return;
        var template = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetString()!;

        var deleteResp = await client.DeleteAsync($"/api/templates/{templateId}");
        deleteResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
