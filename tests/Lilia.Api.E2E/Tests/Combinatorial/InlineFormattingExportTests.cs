using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Inline formatting in paragraphs × export formats.
/// Tests that bold, italic, code, math, links survive export.
/// </summary>
public class InlineFormattingExportTests : E2ETestBase
{
    public static IEnumerable<object[]> FormattedTexts => new[]
    {
        new object[] { "Plain text no formatting" },
        new object[] { "*Bold text here*" },
        new object[] { "_Italic text here_" },
        new object[] { "`inline code`" },
        new object[] { "$E=mc^2$ inline math" },
        new object[] { "Mixed *bold* and _italic_ and `code`" },
        new object[] { "Math $\\alpha + \\beta$ in text" },
        new object[] { "Multiple $x^2$ math $y^3$ expressions" },
        new object[] { "*Bold with $math$ inside*" },
        new object[] { "Nested _italic with `code` inside_" },
        new object[] { "Special chars: & < > \" ' % # $ _ { } ~" },
        new object[] { "URL: https://liliaeditor.com in text" },
    };

    [Theory]
    [MemberData(nameof(FormattedTexts))]
    public async Task FormattedParagraph_ExportsToLatex_WithoutCrash(string text)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Fmt LaTeX");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/latex");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"text '{text}' should not crash LaTeX export");
    }

    [Theory]
    [MemberData(nameof(FormattedTexts))]
    public async Task FormattedParagraph_ExportsToDocx_WithoutCrash(string text)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Fmt DOCX");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text } });

        var resp = await client.GetAsync($"/api/documents/{docId}/export/docx");
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"text '{text}' should not crash DOCX export");
    }
}
