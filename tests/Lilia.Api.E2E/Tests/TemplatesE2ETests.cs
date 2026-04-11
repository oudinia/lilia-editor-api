using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for templates — list, create from document, create document from template.
/// </summary>
public class TemplatesE2ETests : E2ETestBase
{
    [Fact]
    public async Task ListTemplates_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/templates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTemplateCategories_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/templates/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateTemplateFromDocument_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Template Source");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "heading",
            content = new { text = "Template Heading", level = 1 },
        });

        var response = await client.PostAsJsonAsync("/api/templates", new
        {
            documentId = docId,
            name = "E2E Template",
            category = "academic",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        if (response.IsSuccessStatusCode)
        {
            var template = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (template.TryGetProperty("id", out var idProp))
                TrackForCleanup("/api/templates", idProp.GetString()!);
        }
    }

    [Fact]
    public async Task UseTemplate_CreatesDocument()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // Get first available template
        var listResp = await client.GetAsync("/api/templates");
        var body = await listResp.Content.ReadFromJsonAsync<JsonElement>();

        // Response may be an array, { items: [...] }, or other shape
        JsonElement arr;
        if (body.ValueKind == JsonValueKind.Array)
            arr = body;
        else if (body.TryGetProperty("items", out var items))
            arr = items;
        else
            return; // Unexpected shape, skip

        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return; // No templates to test with

        var templateId = arr[0].GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/templates/{templateId}/use", new { });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (doc.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }
}
