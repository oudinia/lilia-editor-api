using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Pagination boundary tests for all list endpoints.
/// </summary>
public class PaginationTests : E2ETestBase
{
    [Theory]
    [InlineData("/api/documents", "page=1&pageSize=1")]
    [InlineData("/api/documents", "page=1&pageSize=100")]
    [InlineData("/api/documents", "page=999&pageSize=10")]
    [InlineData("/api/documents/trash", "page=1&pageSize=10")]
    [InlineData("/api/templates", "")]
    [InlineData("/api/formulas", "page=1&pageSize=5")]
    [InlineData("/api/formulas", "page=999&pageSize=5")]
    [InlineData("/api/snippets", "page=1&pageSize=5")]
    [InlineData("/api/draft-blocks", "page=1&pageSize=5")]
    [InlineData("/api/labels", "")]
    public async Task ListEndpoint_WithPagination_DoesNotCrash(string endpoint, string queryParams)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var url = string.IsNullOrEmpty(queryParams) ? endpoint : $"{endpoint}?{queryParams}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"{url} should not crash");
    }

    [Theory]
    [InlineData("/api/documents", "page=-1&pageSize=10")]
    [InlineData("/api/documents", "page=0&pageSize=0")]
    [InlineData("/api/documents", "page=1&pageSize=1000")]
    [InlineData("/api/formulas", "page=-5&pageSize=-1")]
    [InlineData("/api/snippets", "page=0&pageSize=0")]
    public async Task ListEndpoint_WithInvalidPagination_DoesNotCrash(string endpoint, string queryParams)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{endpoint}?{queryParams}");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"{endpoint}?{queryParams} should not crash");
    }
}
