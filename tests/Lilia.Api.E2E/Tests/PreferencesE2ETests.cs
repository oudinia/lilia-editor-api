using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for user preferences.
/// </summary>
public class PreferencesE2ETests : E2ETestBase
{
    [Fact]
    public async Task GetPreferences_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdatePreferences_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "dark",
            autoSave = true,
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
