using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for document search, filtering, pagination, and sorting.
/// </summary>
public class SearchFilterE2ETests : E2ETestBase
{
    [Fact]
    public async Task SearchDocuments_ByTitle_ReturnsResults()
    {
        using var client = await CreateAuthenticatedClientAsync();
        await CreateTestDocumentAsync(client, "SearchTest Quantum Physics");

        var response = await client.GetAsync("/api/documents?search=Quantum");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.TryGetProperty("items", out var arr) ? arr : body;
        items.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListDocuments_WithPagination_ReturnsPage()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents?page=1&pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("page", out _).Should().BeTrue();
        body.TryGetProperty("totalCount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ListDocuments_SortByTitle_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents?sortBy=title&sortDir=asc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListDocuments_SortByCreatedAt_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents?sortBy=createdAt&sortDir=desc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListDocuments_InvalidSortField_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents?sortBy=nonexistent&sortDir=desc");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ListDocuments_ZeroPageSize_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents?page=0&pageSize=0");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ListDocuments_FilterByLabel_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        // Use a random GUID as label filter — should return empty, not crash
        var response = await client.GetAsync($"/api/documents?labelId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
