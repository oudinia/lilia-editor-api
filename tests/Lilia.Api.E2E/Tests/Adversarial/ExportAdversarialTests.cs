using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// Adversarial export tests — documents with problematic content that could crash exporters.
/// </summary>
public class ExportAdversarialTests : E2ETestBase
{
    [Fact]
    public async Task ExportDocx_WithMaliciousLatex_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Malicious Export");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "equation", content = new { latex = @"\input{/etc/passwd}", display = true } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportLatex_WithXssInContent_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "<script>alert(1)</script>");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "<img src=x onerror=alert(1)>" } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportDocx_WithUnicode_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "数学论文 📐");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "中文测试 العربية 日本語 🧮 émojis ñ ü ö" } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "equation", content = new { latex = @"\alpha \beta \gamma \delta \epsilon", display = true } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportLatex_WithNestedInlineFormatting_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Nested Formatting");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "This is *bold with _nested italic_ inside* and `code` and $x^2$ math." } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportDocx_WithVeryLongParagraph_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Long Paragraph");
        var docId = doc.GetProperty("id").GetString()!;

        var longText = string.Concat(Enumerable.Repeat("This is a very long sentence that repeats many times. ", 200));
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = longText } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportLatex_WithEmptyBlocks_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Empty Blocks");
        var docId = doc.GetProperty("id").GetString()!;

        // Add various empty blocks
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = "paragraph", content = new { text = "" } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = "heading", content = new { text = "", level = 1 } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = "equation", content = new { latex = "", display = true } });
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type = "code", content = new { code = "", language = "" } });

        var latexResp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        latexResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        var docxResp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        docxResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PreviewHtml_WithAllBlockTypes_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "All Types Preview");
        var docId = doc.GetProperty("id").GetString()!;

        var blocks = new (string type, object content)[]
        {
            ("heading", new { text = "H1", level = 1 }),
            ("heading", new { text = "H2", level = 2 }),
            ("paragraph", new { text = "Normal paragraph." }),
            ("equation", new { latex = @"\int f(x) dx", display = true }),
            ("code", new { code = "print('hi')", language = "python" }),
            ("blockquote", new { text = "Quote" }),
            ("list", new { items = new[] { "a", "b" } }),
            ("table", new { rows = new[] { new { cells = new[] { "1", "2" } } } }),
            ("abstract", new { text = "Abstract." }),
            ("theorem", new { statement = "If P then Q", type = "theorem" }),
        };

        foreach (var (type, content) in blocks)
            await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type, content });

        var resp = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
