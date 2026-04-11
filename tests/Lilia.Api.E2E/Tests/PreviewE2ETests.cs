using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for document preview — HTML, LaTeX, sections.
/// </summary>
public class PreviewE2ETests : E2ETestBase
{
    [Fact]
    public async Task PreviewHTML_ReturnsContent()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Preview HTML Test");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Preview content" },
        });

        var response = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PreviewLaTeX_ReturnsContent()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Preview LaTeX Test");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "equation",
            content = new { latex = "E = mc^2", display = true },
        });

        var response = await client.GetAsync($"/api/documents/{docId}/preview/latex");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PreviewSections_ReturnsHeadings()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Preview Sections Test");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "heading",
            content = new { text = "Section One", level = 1 },
        });

        var response = await client.GetAsync($"/api/documents/{docId}/preview/sections");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PageCount_ReturnsNumber()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Page Count Test");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/documents/{docId}/preview/page-count");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
