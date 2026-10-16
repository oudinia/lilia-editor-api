using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// Two-step sign-in (Settings → Security, launch redesign 3b): turned on from a session that just used
/// the authenticator, then enforced: a password alone no longer reaches the account. Connected apps
/// and development identities (the test copy) are let through.
/// </summary>
[Collection("Integration")]
public class TwoStepTests : IntegrationTestBase
{
    public TwoStepTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string Session(params (string Type, DateTime At)[] factors) => JsonSerializer.Serialize(new
    {
        id = "session-test-1",
        authentication_factors = factors.Select(f => new { type = f.Type, last_authenticated_at = f.At.ToString("O") }),
    });

    private HttpClient As(string id, string? session)
    {
        var c = CreateClientAs(id);
        if (session is not null) c.DefaultRequestHeaders.Add("X-Test-StytchSession", session);
        return c;
    }

    private async Task<string> Author()
    {
        var id = $"twostep-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id);
        return id;
    }

    [Fact]
    public async Task Turning_it_on_needs_a_code_just_entered_and_then_a_password_alone_is_refused()
    {
        var id = await Author();
        var now = DateTime.UtcNow;
        var passwordOnly = As(id, Session(("password", now)));
        var withCode = As(id, Session(("password", now.AddMinutes(-1)), ("totp", now)));
        var staleCode = As(id, Session(("password", now), ("totp", now.AddHours(-2))));

        (await passwordOnly.PutAsJsonAsync("/api/me/two-step", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staleCode.PutAsJsonAsync("/api/me/two-step", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "turning it on needs a fresh code");
        (await withCode.PutAsJsonAsync("/api/me/two-step", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await withCode.GetFromJsonAsync<JsonElement>("/api/me/security")).GetProperty("twoStepEnabled").GetBoolean().Should().BeTrue();

        var refused = await passwordOnly.GetAsync("/api/documents");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("TWO_STEP_REQUIRED");
        (await withCode.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staleCode.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK, "a session that used the code once is a two-step session");
        (await As(id, Session(("password", now), ("recovery_codes", now))).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Connected_apps_and_test_identities_are_let_through()
    {
        var id = await Author();
        await using (var db = CreateDbContext())
        {
            (await db.Users.FirstAsync(u => u.Id == id)).TwoStepEnabled = true;
            await db.SaveChangesAsync();
        }
        (await As(id, null).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK, "a development identity (the test copy) has no Stytch session");
        var app = As(id, Session(("password", DateTime.UtcNow)));
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-1");
        (await app.PostAsync("/api/mcp", null)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Turning_it_off_needs_a_two_step_session()
    {
        var id = await Author();
        var now = DateTime.UtcNow;
        await As(id, Session(("totp", now))).PutAsJsonAsync("/api/me/two-step", new { enabled = true });

        (await As(id, Session(("password", now))).PutAsJsonAsync("/api/me/two-step", new { enabled = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As(id, Session(("totp", now.AddHours(-3)))).PutAsJsonAsync("/api/me/two-step", new { enabled = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(id, Session(("password", now))).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_Stytch_admin_credentials_sessions_are_not_offered()
    {
        var id = await Author();
        var client = As(id, null);
        (await client.GetFromJsonAsync<JsonElement>("/api/me/security")).GetProperty("sessionsAvailable").GetBoolean().Should().BeFalse();
        (await client.GetAsync("/api/me/sessions")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/me/sessions/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
