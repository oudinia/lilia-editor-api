using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

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

    // BG-040: when the usersync cache is warm but the user row has been
    // removed (manual cleanup / DB reset without cache invalidation),
    // GetPreferences must not crash with a FK violation on the
    // user_preferences insert. It must detach, clear the cache, and
    // return defaults so the next request re-syncs the user.
    [Fact]
    public async Task GetPreferences_ReturnsDefaults_WhenUserRowMissingButCacheWarm()
    {
        const string userId = "bg040_fkrace_user";

        // Warm the usersync cache — simulates the middleware skipping the
        // user upsert because it synced this user within the last 30 min.
        var cache = Fixture.Factory.Services.GetRequiredService<IDistributedCache>();
        var cacheKey = $"usersync:{userId}";
        await cache.SetAsync(cacheKey, new byte[] { 1 }, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });

        // Intentionally skip SeedUserAsync. No row in `users`.

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync("/api/preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();
        prefs.Should().NotBeNull();
        prefs!.Theme.Should().Be("system");

        // Row must not have been persisted (FK would have forbidden it).
        await using var db = CreateDbContext();
        var saved = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        saved.Should().BeNull();

        // Cache key should be cleared so the next request re-syncs the user.
        var stillCached = await cache.GetAsync(cacheKey);
        stillCached.Should().BeNull();
    }
}
