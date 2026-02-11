using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class PreferencesControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public PreferencesControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetPreferences_ReturnsDefaults_WhenNoneExist()
    {
        await SeedUserAsync(UserId);

        var response = await Client.GetAsync("/api/preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();
        prefs.Should().NotBeNull();
        prefs!.Theme.Should().Be("system");
        prefs.AutoSaveEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePreferences_UpdatesTheme()
    {
        await SeedUserAsync(UserId);

        var response = await Client.PutAsJsonAsync("/api/preferences", new
        {
            theme = "dark"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();
        prefs!.Theme.Should().Be("dark");
    }

    [Fact]
    public async Task UpdatePreferences_PartialUpdate_PreservesOtherFields()
    {
        await SeedUserAsync(UserId);

        // Set theme
        await Client.PutAsJsonAsync("/api/preferences", new { theme = "dark" });

        // Update only font
        var response = await Client.PutAsJsonAsync("/api/preferences", new
        {
            defaultFontFamily = "monospace"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();
        prefs!.Theme.Should().Be("dark"); // Should be preserved
        prefs.DefaultFontFamily.Should().Be("monospace");
    }

    [Fact]
    public async Task GetPreferences_Returns401_WhenAnonymous()
    {
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync("/api/preferences");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
