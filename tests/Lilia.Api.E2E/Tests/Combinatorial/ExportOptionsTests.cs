using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// LaTeX export with various options — document class, paper size, structure, etc.
/// </summary>
public class ExportOptionsTests : E2ETestBase
{
    private async Task<string> CreateDocWithContentAsync(HttpClient client)
    {
        var doc = await CreateTestDocumentAsync(client, "Export Options");
        var docId = doc.GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "heading", content = new { text = "Title", level = 1 } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Content paragraph." } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "equation", content = new { latex = @"\sum_{i=1}^n i", display = true } });
        return docId;
    }

    [Theory]
    [InlineData("article")]
    [InlineData("report")]
    [InlineData("book")]
    [InlineData("letter")]
    public async Task ExportLatex_DocumentClass_DoesNotCrash(string docClass)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var docId = await CreateDocWithContentAsync(client);
        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex?documentClass={docClass}");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"documentClass={docClass} should not crash");
    }

    [Theory]
    [InlineData("a4paper")]
    [InlineData("letterpaper")]
    [InlineData("a5paper")]
    public async Task ExportLatex_PaperSize_DoesNotCrash(string paperSize)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var docId = await CreateDocWithContentAsync(client);
        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex?paperSize={paperSize}");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"paperSize={paperSize} should not crash");
    }

    [Theory]
    [InlineData("10pt")]
    [InlineData("11pt")]
    [InlineData("12pt")]
    public async Task ExportLatex_FontSize_DoesNotCrash(string fontSize)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var docId = await CreateDocWithContentAsync(client);
        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex?fontSize={fontSize}");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"fontSize={fontSize} should not crash");
    }

    [Theory]
    [InlineData("single")]
    [InlineData("multi")]
    public async Task ExportLatex_Structure_DoesNotCrash(string structure)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var docId = await CreateDocWithContentAsync(client);
        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex?structure={structure}");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"structure={structure} should not crash");
    }

    [Fact]
    public async Task ExportLatex_AllOptionsAtOnce_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var docId = await CreateDocWithContentAsync(client);
        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex?documentClass=report&fontSize=12pt&paperSize=letterpaper&structure=multi&includePhysics=true&includeChemistry=true&bibliographyStyle=ieeetr&lineSpacing=1.5");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportLatex_InvalidOptions_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var docId = await CreateDocWithContentAsync(client);
        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex?documentClass=invalid&fontSize=999pt&paperSize=huge");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
