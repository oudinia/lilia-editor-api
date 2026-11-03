using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Features.Accounts;

/// <summary>
/// GET /api/auth/me and POST /api/auth/sign-out (3 Nov 2026): no test called them, which the test
/// picker's URL ownership (API #419) brought to light. "Me" answers with the signed-in person's own
/// account (the sign-in sync keeps it in step with the token), or 404 while the sync has none stored. Sign-out asks Stytch to revoke the session the
/// request carries, and never fails the person's sign-out: Stytch is a stub here that records what it
/// was sent.
/// </summary>
public class AuthSessionEndpointTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = new();
    private readonly StytchStub _stytch = new();

    public AuthSessionEndpointTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    /// <summary>A host with the real user service and Stytch's admin API answered by the stub (or not configured at all).</summary>
    private HttpClient Caller(string userId, bool stytchConfigured = true, string? email = null, string? name = null)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Stytch:ProjectId", stytchConfigured ? "project-test-001" : "");
            b.UseSetting("Stytch:Secret", stytchConfigured ? "secret-test-001" : "");
            b.UseSetting("Stytch:ApiBase", "https://stytch.stub.test");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IUserService>();
                s.AddScoped<IUserService, UserService>();
                s.ConfigureHttpClientDefaults(h => h.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_stytch)));
            });
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", email ?? $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", name ?? userId);
        return c;
    }

    private static HttpRequestMessage SignOut(string? sessionJwt)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out");
        if (sessionJwt is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionJwt);
        return req;
    }

    [Fact]
    public async Task Me_is_the_signed_in_persons_own_account()
    {
        await SeedUserAsync("me-ada", "ada@example.org", "Ada Lovelace");
        await SeedUserAsync("me-grace", "grace@example.org", "Grace Hopper");

        var res = await Caller("me-ada", email: "ada@example.org", name: "Ada Lovelace").GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be("me-ada");
        body.GetProperty("email").GetString().Should().Be("ada@example.org");
        body.GetProperty("name").GetString().Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task Me_while_the_sign_in_sync_has_no_account_stored_is_404_user_not_synced()
    {
        // The shared test host's user service stores nothing, as a sync that has not run yet.
        var res = await CreateClientAs("me-new").GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("user_not_synced");
    }

    [Fact]
    public async Task Sign_out_revokes_the_session_the_request_carries_with_Lilias_Stytch_credentials()
    {
        var res = await Caller("so-ada").SendAsync(SignOut("session-jwt-ada"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revoked").GetBoolean().Should().BeTrue();
        var sent = _stytch.Requests.Should().ContainSingle().Subject;
        sent.Path.Should().Be("/v1/sessions/revoke");
        sent.Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("project-test-001:secret-test-001")));
        JsonDocument.Parse(sent.Body).RootElement.GetProperty("session_jwt").GetString().Should().Be("session-jwt-ada");
    }

    [Fact]
    public async Task Sign_out_of_a_session_Stytch_already_ended_still_succeeds()
    {
        _stytch.Status = HttpStatusCode.BadRequest;

        var res = await Caller("so-grace").SendAsync(SignOut("session-jwt-expired"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("revoked").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("stytch_error");
    }

    [Fact]
    public async Task Sign_out_without_Stytch_credentials_succeeds_and_asks_nobody()
    {
        var res = await Caller("so-noconfig", stytchConfigured: false).SendAsync(SignOut("session-jwt-x"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString().Should().Be("service_unavailable");
        _stytch.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Sign_out_without_a_session_token_is_already_done_and_asks_nobody()
    {
        var res = await Caller("so-notoken").SendAsync(SignOut(null));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revoked").GetBoolean().Should().BeTrue();
        _stytch.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_sign_in_both_are_401()
    {
        var anonymous = CreateAnonymousClient();
        (await anonymous.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync("/api/auth/sign-out", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record Sent(string Path, string? Authorization, string Body);

    private sealed class StytchStub
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<Sent> Requests { get; } = new();
    }

    /// <summary>Every outgoing call of the host lands here; only Stytch's host is answered.</summary>
    private sealed class StubHandler(StytchStub stytch) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "stytch.stub.test")
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (stytch.Requests)
                stytch.Requests.Add(new Sent(request.RequestUri.AbsolutePath, request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(stytch.Status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }
}
