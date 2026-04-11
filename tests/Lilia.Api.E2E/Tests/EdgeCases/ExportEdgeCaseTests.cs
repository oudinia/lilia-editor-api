using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.EdgeCases;

/// <summary>
/// Edge cases for Export API — empty documents, mixed content, large documents.
/// </summary>
public class ExportEdgeCaseTests : E2ETestBase
{
    [Fact]
    public async Task ExportLaTeX_EmptyDocument_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Empty Export");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/documents/{docId}/export/latex");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportPDF_EmptyDocument_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Empty PDF");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/documents/{docId}/export/pdf");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportDOCX_EmptyDocument_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Empty DOCX");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/documents/{docId}/export/docx");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportLaTeX_NonExistentDoc_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}/export/latex");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportDOCX_MixedBlockTypes_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Mixed DOCX");
        var docId = doc.GetProperty("id").GetString()!;

        // Add various block types that could trigger edge cases
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "heading", content = new { text = "Title", level = 1 } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Normal text with *bold* and _italic_." } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "equation", content = new { latex = @"\frac{a}{b} + \sqrt{c}", display = true } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "code", content = new { code = "fn main() { println!(\"hello\"); }", language = "rust" } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "blockquote", content = new { text = "A wise quote" } });

        var response = await client.GetAsync($"/api/documents/{docId}/export/docx");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportLaTeX_SpecialCharactersInTitle_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Title with & < > \" ' % # $ _ { } ~ ^");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Content" } });

        var response = await client.GetAsync($"/api/documents/{docId}/export/latex");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PreviewHTML_WithBrokenLatex_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Broken LaTeX Preview");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "equation", content = new { latex = @"\frac{broken", display = true } });

        var response = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
