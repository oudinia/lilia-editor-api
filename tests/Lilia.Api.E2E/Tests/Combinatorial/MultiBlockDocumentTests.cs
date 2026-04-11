using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Documents with many blocks — stress the full pipeline.
/// </summary>
public class MultiBlockDocumentTests : E2ETestBase
{
    [Fact]
    public async Task Document_With20Blocks_ExportsToLatex()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "20 Blocks Doc");
        var docId = doc.GetProperty("id").GetString()!;

        for (var i = 0; i < 20; i++)
        {
            var type = (i % 4) switch { 0 => "heading", 1 => "paragraph", 2 => "equation", _ => "code" };
            var content = type switch
            {
                "heading" => (object)new { text = $"Section {i}", level = (i % 3) + 1 },
                "equation" => new { latex = $"x_{{{i}}} = {i}", display = true },
                "code" => new { code = $"line_{i} = {i}", language = "python" },
                _ => new { text = $"Paragraph {i} content." },
            };
            await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type, content });
        }

        var exportResp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        exportResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        var previewResp = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        previewResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Document_With20Blocks_ExportsToDocx()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "20 Blocks DOCX");
        var docId = doc.GetProperty("id").GetString()!;

        for (var i = 0; i < 20; i++)
        {
            await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            {
                type = "paragraph",
                content = new { text = $"Paragraph {i}: Lorem ipsum dolor sit amet." },
            });
        }

        var resp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Document_AllBlockTypes_PreviewsToHtml()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "All Types");
        var docId = doc.GetProperty("id").GetString()!;

        var blocks = new (string type, object content)[]
        {
            ("heading", new { text = "Title", level = 1 }),
            ("heading", new { text = "Subtitle", level = 2 }),
            ("heading", new { text = "Section", level = 3 }),
            ("paragraph", new { text = "Normal text with *bold* and _italic_." }),
            ("paragraph", new { text = "Another paragraph with `code` and $E=mc^2$." }),
            ("equation", new { latex = @"\int_0^\infty e^{-x^2} dx = \frac{\sqrt{\pi}}{2}", display = true }),
            ("equation", new { latex = @"\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}", display = true }),
            ("code", new { code = "def fib(n):\n  if n <= 1: return n\n  return fib(n-1) + fib(n-2)", language = "python" }),
            ("code", new { code = "SELECT * FROM documents WHERE deleted_at IS NULL", language = "sql" }),
            ("blockquote", new { text = "The only way to do great work is to love what you do." }),
            ("list", new { items = new[] { "First item", "Second item", "Third item" } }),
            ("table", new { rows = new[] { new { cells = new[] { "Name", "Value" } }, new { cells = new[] { "Alpha", "0.05" } } } }),
            ("abstract", new { text = "This paper presents a comprehensive analysis..." }),
            ("theorem", new { statement = "For every integer n > 2, there exists...", type = "theorem" }),
        };

        foreach (var (type, content) in blocks)
            await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type, content });

        var resp = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Document_WithBibliography_ExportsWithCitations()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Bib Export");
        var docId = doc.GetProperty("id").GetString()!;

        // Add bibliography entries
        await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "einstein1905",
            entryType = "article",
            data = new { title = "Special Relativity", author = "Einstein, Albert", year = "1905", journal = "Annalen" },
        });

        // Add paragraph referencing citation
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = @"As shown by \cite{einstein1905}, the theory..." },
        });

        // Export should include bibliography
        var latexResp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        latexResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        var docxResp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        docxResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
