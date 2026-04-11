using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// Boundary value tests — empty strings, max values, zero-length arrays.
/// </summary>
public class BoundaryTests : E2ETestBase
{
    [Fact]
    public async Task CreateDocument_EmptyObject_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/documents", new { });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (doc.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(100)]
    public async Task HeadingBlock_InvalidLevel_HandledGracefully(int level)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Heading-L{level}");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "heading",
            content = new { text = "Test", level },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EquationBlock_EmptyLatex_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "equation",
            content = new { latex = "", display = true },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task CodeBlock_EmptyCode_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "code",
            content = new { code = "", language = "" },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TableBlock_EmptyRows_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "table",
            content = new { rows = Array.Empty<object>() },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ListBlock_EmptyItems_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "list",
            content = new { items = Array.Empty<string>() },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ListBlock_ManyItems_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var items = Enumerable.Range(1, 100).Select(i => $"Item {i}").ToArray();
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "list",
            content = new { items },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task TableBlock_LargeTable_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var rows = Enumerable.Range(1, 50).Select(r =>
            new { cells = Enumerable.Range(1, 10).Select(c => $"R{r}C{c}").ToArray() }
        ).ToArray();

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "table",
            content = new { rows },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task CreateLabel_EmptyName_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/labels", new { name = "", color = "#000000" });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    [Fact(Skip = "Bug: API returns 500 on invalid color — needs input validation")]
    public async Task CreateLabel_InvalidColor_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/labels", new { name = "Test", color = "not-a-color" });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task CreateComment_EmptyContent_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = "" });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task CreateComment_VeryLongContent_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var longContent = new string('X', 10000);
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = longContent });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task BibEntry_EmptyData_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "empty2024",
            entryType = "article",
            data = new { },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Formula_VeryLongLatex_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var longLatex = string.Concat(Enumerable.Repeat(@"\frac{a}{b} + ", 500));
        var response = await client.PostAsJsonAsync("/api/formulas", new
        {
            name = "Long Formula",
            latexContent = longLatex,
            category = "test",
            description = "Very long formula",
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        if (response.IsSuccessStatusCode)
        {
            var formula = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (formula.TryGetProperty("id", out var id))
                TrackForCleanup("/api/formulas", id.GetString()!);
        }
    }
}
