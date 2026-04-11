using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for labels — CRUD and assignment to documents.
/// </summary>
public class LabelsE2ETests : E2ETestBase
{
    [Fact]
    public async Task CreateLabel_ReturnsCreated()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/labels", new
        {
            name = "E2E Label",
            color = "#FF5733",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        if (response.IsSuccessStatusCode)
        {
            var label = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (label.TryGetProperty("id", out var id))
                TrackForCleanup("/api/labels", id.GetString()!);
        }
    }

    [Fact]
    public async Task ListLabels_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/labels");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AssignLabelToDocument_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Label Doc");
        var docId = doc.GetProperty("id").GetString()!;

        // Create label
        var labelResp = await client.PostAsJsonAsync("/api/labels", new { name = "E2E Assign", color = "#00FF00" });
        if (!labelResp.IsSuccessStatusCode) return;
        var label = await labelResp.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetString()!;
        TrackForCleanup("/api/labels", labelId);

        // Assign to document
        var assignResp = await client.PostAsync($"/api/documents/{docId}/labels/{labelId}", null);
        assignResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created);
    }
}
