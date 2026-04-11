using System.Net;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// Every authenticated endpoint × anonymous request.
/// Verifies all protected endpoints return 401, not 500.
/// </summary>
public class AuthEndpointTests : E2ETestBase
{
    [Theory]
    [InlineData("GET", "/api/documents")]
    [InlineData("POST", "/api/documents")]
    [InlineData("GET", "/api/documents/trash")]
    [InlineData("GET", "/api/templates")]
    [InlineData("GET", "/api/formulas")]
    [InlineData("GET", "/api/snippets")]
    [InlineData("GET", "/api/draft-blocks")]
    [InlineData("GET", "/api/labels")]
    [InlineData("GET", "/api/preferences")]
    [InlineData("PUT", "/api/preferences")]
    public async Task AuthenticatedEndpoint_WithoutToken_Returns401(string method, string endpoint)
    {
        using var client = CreateClient();
        var response = method switch
        {
            "GET" => await client.GetAsync(endpoint),
            "POST" => await client.PostAsync(endpoint, new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            "PUT" => await client.PutAsync(endpoint, new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            "DELETE" => await client.DeleteAsync(endpoint),
            _ => throw new ArgumentException($"Unknown method: {method}")
        };
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {endpoint} should require auth");
    }

    [Theory]
    [InlineData("GET", "/api/documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/documents/00000000-0000-0000-0000-000000000001/blocks")]
    [InlineData("GET", "/api/documents/00000000-0000-0000-0000-000000000001/blocks")]
    [InlineData("GET", "/api/documents/00000000-0000-0000-0000-000000000001/bibliography")]
    [InlineData("GET", "/api/documents/00000000-0000-0000-0000-000000000001/comments")]
    [InlineData("GET", "/api/documents/00000000-0000-0000-0000-000000000001/versions")]
    [InlineData("GET", "/api/studio/00000000-0000-0000-0000-000000000001/tree")]
    public async Task DocumentEndpoint_WithoutToken_Returns401(string method, string endpoint)
    {
        using var client = CreateClient();
        var response = method switch
        {
            "GET" => await client.GetAsync(endpoint),
            "POST" => await client.PostAsync(endpoint, new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            "PUT" => await client.PutAsync(endpoint, new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            "DELETE" => await client.DeleteAsync(endpoint),
            _ => throw new ArgumentException($"Unknown method: {method}")
        };
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {endpoint} should require auth");
    }
}
