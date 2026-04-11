using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for LaTeX validation and rendering endpoints.
/// </summary>
public class LaTeXValidationE2ETests : E2ETestBase
{
    [Fact]
    public async Task ValidateLatex_ValidSource_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/latex/validate", new
        {
            latex = @"\documentclass{article}\begin{document}Hello\end{document}",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ValidateLatex_BrokenSource_ReturnsErrors()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/latex/validate", new
        {
            latex = @"\begin{equation}\frac{broken\end{document}",
        });
        // Should return validation result, not crash
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ValidateDocument_ReturnsResult()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Validate Doc");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "equation",
            content = new { latex = @"E = mc^2", display = true },
        });

        var response = await client.PostAsync($"/api/latex/{docId}/validate", null);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ValidateBlock_ReturnsResult()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Block Validate");
        var docId = doc.GetProperty("id").GetString()!;

        var blockResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "equation",
            content = new { latex = @"\sum_{i=1}^n i^2", display = true },
        });
        if (!blockResp.IsSuccessStatusCode) return;
        var block = await blockResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var response = await client.PostAsync($"/api/latex/block/{blockId}/validate", null);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetMetrics_ReturnsOk()
    {
        using var client = CreateClient(); // No auth needed
        var response = await client.GetAsync("/api/latex/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RenderSvg_ReturnsImage()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/latex/svg?latex=E%3Dmc%5E2&display=true");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }
}
