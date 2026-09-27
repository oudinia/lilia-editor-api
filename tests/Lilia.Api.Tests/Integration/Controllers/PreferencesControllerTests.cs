using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    // Personality: how the editor's "saved" moment looks. "pro" by default.
    [Fact]
    public async Task GetPreferences_PersonalityIsPro_ForAFreshUser()
    {
        await SeedUserAsync(UserId);

        var response = await Client.GetAsync("/api/preferences");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"personality\":\"pro\"");
        var prefs = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();
        prefs!.Personality.Should().Be("pro");
    }

    [Fact]
    public async Task UpdatePreferences_SetsPersonalityToFun_AndKeepsOtherFields()
    {
        await SeedUserAsync(UserId);
        await Client.PutAsJsonAsync("/api/preferences", new { theme = "dark", defaultFontFamily = "serif" });

        var put = await Client.PutAsJsonAsync("/api/preferences", new { personality = "FUN" });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await Client.GetFromJsonAsync<UserPreferencesDto>("/api/preferences");
        prefs!.Personality.Should().Be("fun"); // stored lowercase
        prefs.Theme.Should().Be("dark");
        prefs.DefaultFontFamily.Should().Be("serif");
        prefs.AutoSaveEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePreferences_WithoutPersonality_LeavesItUnchanged()
    {
        await SeedUserAsync(UserId);
        await Client.PutAsJsonAsync("/api/preferences", new { personality = "fun" });

        var put = await Client.PutAsJsonAsync("/api/preferences", new { theme = "light" });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await Client.GetFromJsonAsync<UserPreferencesDto>("/api/preferences");
        prefs!.Personality.Should().Be("fun");
        prefs.Theme.Should().Be("light");
    }

    [Fact]
    public async Task UpdatePreferences_Returns400_ForAnUnknownPersonality_AndChangesNothing()
    {
        await SeedUserAsync(UserId);
        await Client.PutAsJsonAsync("/api/preferences", new { theme = "dark", personality = "fun" });

        var put = await Client.PutAsJsonAsync("/api/preferences", new { theme = "light", personality = "loud" });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await put.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("personality");

        var prefs = await Client.GetFromJsonAsync<UserPreferencesDto>("/api/preferences");
        prefs!.Personality.Should().Be("fun");
        prefs.Theme.Should().Be("dark");
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

    // ── Pinned tools (Lilia menu) — stored under "_pinnedTools" in the
    // keyboard_shortcuts jsonb, no migration; never shown as a shortcut. ──

    private async Task<HttpClient> PinsClientAsync()
    {
        var userId = $"test_pins_{Guid.NewGuid():N}";
        await SeedUserAsync(userId);
        return CreateClientAs(userId);
    }

    [Fact]
    public async Task PinnedTools_is_null_until_set()
    {
        using var client = await PinsClientAsync();

        var res = await client.GetAsync("/api/preferences");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await res.Content.ReadFromJsonAsync<JsonElement>();
        raw.TryGetProperty("pinnedTools", out _).Should().BeFalse("null is omitted");
        (await client.GetFromJsonAsync<UserPreferencesDto>("/api/preferences"))!.PinnedTools.Should().BeNull();
    }

    [Fact]
    public async Task PinnedTools_round_trip_in_order_and_stay_out_of_keyboardShortcuts()
    {
        using var client = await PinsClientAsync();
        (await client.PutAsJsonAsync("/api/preferences/shortcuts",
            new { shortcuts = new { save = "Mod-s" } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await client.PutAsJsonAsync("/api/preferences",
            new { pinnedTools = new[] { "tables", "documents", "ask-lilia" } });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<UserPreferencesDto>())!.PinnedTools
            .Should().Equal("tables", "documents", "ask-lilia");

        var raw = await client.GetFromJsonAsync<JsonElement>("/api/preferences");
        raw.GetProperty("pinnedTools").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("tables", "documents", "ask-lilia");
        var shortcuts = raw.GetProperty("keyboardShortcuts");
        shortcuts.TryGetProperty("_pinnedTools", out _).Should().BeFalse();
        shortcuts.GetProperty("save").GetString().Should().Be("Mod-s", "pinning leaves the shortcuts alone");

        var viaShortcuts = await client.GetFromJsonAsync<JsonElement>("/api/preferences/shortcuts");
        viaShortcuts.GetProperty("keyboardShortcuts").TryGetProperty("_pinnedTools", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PinnedTools_empty_list_means_pinned_nothing()
    {
        using var client = await PinsClientAsync();
        await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = new[] { "tables" } });

        var put = await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = Array.Empty<string>() });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<UserPreferencesDto>("/api/preferences"))!.PinnedTools
            .Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task PinnedTools_survive_an_unrelated_update_and_a_shortcuts_replace()
    {
        using var client = await PinsClientAsync();
        await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = new[] { "equations", "import" } });

        await client.PutAsJsonAsync("/api/preferences", new { theme = "dark" });
        var shortcutsPut = await client.PutAsJsonAsync("/api/preferences/shortcuts",
            new { shortcuts = new { bold = "Mod-b", _pinnedTools = new[] { "sneaky" } } });

        shortcutsPut.StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await client.GetFromJsonAsync<JsonElement>("/api/preferences");
        prefs.GetProperty("theme").GetString().Should().Be("dark");
        prefs.GetProperty("pinnedTools").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("equations", "import");
        var shortcuts = prefs.GetProperty("keyboardShortcuts");
        shortcuts.GetProperty("bold").GetString().Should().Be("Mod-b");
        shortcuts.TryGetProperty("_pinnedTools", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Tables")]
    [InlineData("1tables")]
    [InlineData("tables!")]
    [InlineData("")]
    [InlineData("a-name-that-is-far-too-long-to-be-a-tool")]
    public async Task PinnedTools_with_an_invalid_key_is_400(string key)
    {
        using var client = await PinsClientAsync();

        var res = await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = new[] { "tables", key } });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .Should().NotBeNullOrEmpty();
        (await client.GetFromJsonAsync<UserPreferencesDto>("/api/preferences"))!.PinnedTools.Should().BeNull();
    }

    [Fact]
    public async Task PinnedTools_more_than_12_or_duplicated_is_400()
    {
        using var client = await PinsClientAsync();
        var thirteen = Enumerable.Range(0, 13).Select(i => "tool-" + (char)('a' + i)).ToArray();

        (await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = thirteen }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = thirteen[..12] }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "12 is the limit, not over it");
        (await client.PutAsJsonAsync("/api/preferences", new { pinnedTools = new[] { "tables", "tables" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
