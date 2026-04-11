using System.Net;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// Smoke tests to verify the API is reachable and responding.
/// </summary>
public class HealthTests : E2ETestBase
{
    [Fact]
    public async Task Swagger_ReturnsOk()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Documents_RequiresAuth()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/documents");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Documents_ReturnsOk_WithAuth()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
