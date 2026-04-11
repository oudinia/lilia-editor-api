using System.Net;
using System.Text;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// Malformed JSON, empty bodies, wrong content types.
/// Ensures no endpoint crashes on bad input.
/// </summary>
public class MalformedInputTests : E2ETestBase
{
    [Theory]
    [InlineData("/api/documents", "")]
    [InlineData("/api/documents", "not json")]
    [InlineData("/api/documents", "{")]
    [InlineData("/api/documents", "[]")]
    [InlineData("/api/documents", "null")]
    [InlineData("/api/documents", "true")]
    [InlineData("/api/documents", "12345")]
    public async Task PostEndpoint_MalformedJson_DoesNotCrash(string endpoint, string body)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(endpoint, content);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"POST {endpoint} with '{body}' should not crash");
    }

    [Theory]
    [InlineData("/api/labels", "")]
    [InlineData("/api/labels", "not json")]
    [InlineData("/api/labels", "{\"name\":null}")]
    [InlineData("/api/formulas", "")]
    [InlineData("/api/formulas", "{}")]
    [InlineData("/api/snippets", "")]
    [InlineData("/api/snippets", "{}")]
    [InlineData("/api/draft-blocks", "")]
    [InlineData("/api/draft-blocks", "{}")]
    public async Task PostEndpoint_EmptyOrMinimalJson_DoesNotCrash(string endpoint, string body)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(endpoint, content);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"POST {endpoint} with '{body}' should not crash");
    }

    [Fact]
    public async Task PostDocument_WrongContentType_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var content = new StringContent("title=test", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync("/api/documents", content);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PostDocument_XmlBody_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var content = new StringContent("<document><title>test</title></document>", Encoding.UTF8, "application/xml");
        var response = await client.PostAsync("/api/documents", content);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PostDocument_EmptyBody_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/documents", null);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PutDocument_MalformedJson_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var content = new StringContent("{broken", Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"/api/documents/{docId}", content);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PostBlock_MalformedContent_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var body = new StringContent("""{"type":"paragraph","content":{broken""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/documents/{docId}/blocks", body);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PostBibliography_MalformedBibTeX_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsync($"/api/documents/{docId}/bibliography/import",
            new StringContent("""{"bibTexContent":"@article{broken, no closing brace"}""",
                Encoding.UTF8, "application/json"));
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ValidateLatex_EmptyString_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/latex/validate",
            new StringContent("""{"latex":""}""", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ValidateLatex_VeryLongInput_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var longLatex = string.Concat(Enumerable.Repeat(@"\frac{a}{b} + ", 1000));
        var response = await client.PostAsync("/api/latex/validate",
            new StringContent($"{{\"latex\":\"{longLatex}\"}}", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
