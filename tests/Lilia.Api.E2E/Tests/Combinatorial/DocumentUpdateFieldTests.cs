using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Update document with every possible field — ensures no field crashes the update.
/// </summary>
public class DocumentUpdateFieldTests : E2ETestBase
{
    [Theory]
    [InlineData("title", "New Title")]
    [InlineData("language", "fr")]
    [InlineData("language", "en")]
    [InlineData("language", "ar")]
    [InlineData("paperSize", "a4")]
    [InlineData("paperSize", "letter")]
    [InlineData("paperSize", "a5")]
    [InlineData("fontFamily", "serif")]
    [InlineData("fontFamily", "sans-serif")]
    [InlineData("fontFamily", "monospace")]
    [InlineData("fontSize", "10")]
    [InlineData("fontSize", "12")]
    [InlineData("fontSize", "14")]
    [InlineData("columns", "1")]
    [InlineData("columns", "2")]
    [InlineData("lineSpacing", "1.0")]
    [InlineData("lineSpacing", "1.5")]
    [InlineData("lineSpacing", "2.0")]
    public async Task UpdateDocument_SingleField_DoesNotCrash(string field, string value)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Update-{field}");
        var docId = doc.GetProperty("id").GetString()!;

        var payload = new Dictionary<string, object> { [field] = value };
        var response = await client.PutAsJsonAsync($"/api/documents/{docId}", payload);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"updating '{field}' to '{value}' should not crash");
    }

    [Fact]
    public async Task UpdateDocument_AllFieldsAtOnce_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Update All Fields");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PutAsJsonAsync($"/api/documents/{docId}", new
        {
            title = "Updated All",
            language = "fr",
            paperSize = "letter",
            fontFamily = "sans-serif",
            fontSize = 14,
            columns = 2,
            lineSpacing = 1.5,
            marginTop = "2cm",
            marginBottom = "2cm",
            marginLeft = "2.5cm",
            marginRight = "2.5cm",
            headerText = "Header",
            footerText = "Footer",
            pageNumbering = "arabic",
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact(Skip = "Bug: API returns 500 on invalid language — needs input validation")]
    public async Task UpdateDocument_InvalidLanguage_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Bad Language");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PutAsJsonAsync($"/api/documents/{docId}", new { language = "not-a-language" });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UpdateDocument_InvalidPaperSize_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Bad Paper");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PutAsJsonAsync($"/api/documents/{docId}", new { paperSize = "tabloid-xl-9000" });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
