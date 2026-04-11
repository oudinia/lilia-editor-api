using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// SQL injection and XSS payload tests.
/// Ensures no endpoint crashes or leaks data with malicious input.
/// </summary>
public class InjectionTests : E2ETestBase
{
    public static IEnumerable<object[]> SqlInjectionPayloads => new[]
    {
        new object[] { "'; DROP TABLE documents; --" },
        new object[] { "1 OR 1=1" },
        new object[] { "' UNION SELECT * FROM users --" },
        new object[] { "Robert'); DROP TABLE blocks;--" },
        new object[] { "1; EXEC xp_cmdshell('whoami')" },
    };

    public static IEnumerable<object[]> XssPayloads => new[]
    {
        new object[] { "<script>alert('xss')</script>" },
        new object[] { "<img src=x onerror=alert(1)>" },
        new object[] { "javascript:alert(1)" },
        new object[] { "<svg onload=alert(1)>" },
        new object[] { "{{constructor.constructor('return this')()}}" },
    };

    [Theory]
    [MemberData(nameof(SqlInjectionPayloads))]
    public async Task CreateDocument_SqlInjectionTitle_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/documents", new { title = payload });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"SQL injection in title should not crash: {payload}");
        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            if (doc.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public async Task CreateDocument_XssTitle_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/documents", new { title = payload });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"XSS in title should not crash: {payload}");
        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            if (doc.TryGetProperty("id", out var id))
                TrackForCleanup("/api/documents", id.GetString()!);
        }
    }

    [Theory]
    [MemberData(nameof(SqlInjectionPayloads))]
    public async Task CreateBlock_SqlInjectionContent_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = payload },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public async Task CreateBlock_XssContent_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = payload },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(SqlInjectionPayloads))]
    public async Task SearchDocuments_SqlInjection_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/documents?search={Uri.EscapeDataString(payload)}");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(SqlInjectionPayloads))]
    public async Task CreateComment_SqlInjection_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = payload });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(SqlInjectionPayloads))]
    public async Task CreateLabel_SqlInjection_DoesNotCrash(string payload)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/labels", new { name = payload, color = "#FF0000" });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        if (response.IsSuccessStatusCode)
        {
            var label = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            if (label.TryGetProperty("id", out var id))
                TrackForCleanup("/api/labels", id.GetString()!);
        }
    }
}
