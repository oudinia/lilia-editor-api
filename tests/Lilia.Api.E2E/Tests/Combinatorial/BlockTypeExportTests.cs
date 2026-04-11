using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Every block type × every export format.
/// Ensures no block type crashes any exporter.
/// </summary>
public class BlockTypeExportTests : E2ETestBase
{
    public static IEnumerable<object[]> BlockTypes => new[]
    {
        new object[] { "paragraph", """{"text":"Hello world"}""" },
        new object[] { "heading", """{"text":"Title","level":1}""" },
        new object[] { "heading", """{"text":"Subtitle","level":2}""" },
        new object[] { "heading", """{"text":"Section","level":3}""" },
        new object[] { "equation", """{"latex":"E=mc^2","display":true}""" },
        new object[] { "equation", """{"latex":"\\alpha + \\beta","display":false}""" },
        new object[] { "code", """{"code":"fn main() {}","language":"rust"}""" },
        new object[] { "code", """{"code":"SELECT * FROM t","language":"sql"}""" },
        new object[] { "blockquote", """{"text":"A wise quote"}""" },
        new object[] { "list", """{"items":["one","two","three"]}""" },
        new object[] { "table", """{"rows":[{"cells":["A","B"]},{"cells":["1","2"]}]}""" },
        new object[] { "abstract", """{"text":"Abstract content here."}""" },
        new object[] { "theorem", """{"statement":"If P then Q","type":"theorem"}""" },
        new object[] { "figure", """{"url":"","caption":"Figure 1"}""" },
    };

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public async Task BlockType_ExportsToLatex_WithoutCrash(string blockType, string contentJson)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"LaTeX-{blockType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = blockType, content });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"{blockType} should export to LaTeX");
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public async Task BlockType_ExportsToDocx_WithoutCrash(string blockType, string contentJson)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"DOCX-{blockType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = blockType, content });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"{blockType} should export to DOCX");
    }

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public async Task BlockType_PreviewsToHtml_WithoutCrash(string blockType, string contentJson)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"HTML-{blockType}");
        var docId = doc.GetProperty("id").GetString()!;

        var content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = blockType, content });

        var resp = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"{blockType} should preview to HTML");
    }
}
