using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="Lilia.Api.Controllers.StytchWebhookController"/>
/// (route <c>POST /api/webhooks/stytch/email</c>).
///
/// History: this suite originally targeted a Kinde-based
/// <c>WebhooksController</c> that JWKS-verified JWT payloads and
/// published <c>UserCreatedEvent</c>. That controller was deleted in the
/// "drop Kinde — single Stytch JWT validation" refactor (b456433) and
/// later replaced by <see cref="StytchWebhookController"/>, which uses
/// Svix HMAC-SHA256 signatures and sends the branded verification email
/// directly via <see cref="IEmailService.SendStytchVerificationAsync"/>
/// (no event publish). The suite was left orphaned referencing the
/// deleted types; it is rewritten here against the real production
/// controller.
///
/// Behavior under test:
///   - direct.user.create (unverified email, valid Svix sig) → mints a
///     magic link and sends a verification email.
///   - Pre-verified email (social signup) → no verification email.
///   - Unknown event type → 200, no email.
///   - Empty body → 400.
///   - Bad/absent Svix signature (RequireSignature=true) → 401.
///
/// Signing: the controller verifies HMAC-SHA256 over
/// "{svix-id}.{svix-timestamp}.{body}" using a whsec_-prefixed,
/// base64-encoded secret. We inject a known secret via
/// <see cref="StytchWebhookSettings"/> and sign payloads accordingly.
///
/// Magic-link minting: the controller calls Stytch's admin API
/// (POST /v1/magic_links). With no Stytch:ProjectId / Stytch:Secret
/// configured it short-circuits to null *before* any HTTP call and
/// logs, so no email is sent. To exercise the email path we point
/// Stytch:ApiBase at a local stub server that returns a token.
/// </summary>
[Collection("Integration")]
public class WebhooksControllerTests : IntegrationTestBase
{
    private const string TestSecret = "whsec_" + "c2VjcmV0LWtleS1mb3Itc3ZpeC1obWFjLXRlc3Q="; // base64 of arbitrary bytes

    public WebhooksControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Stytch_UserCreate_UnverifiedEmail_SendsVerificationEmail()
    {
        var emailMock = new Mock<IEmailService>();
        using var stytch = StubStytchAdmin(token: "magic-token-abc");
        var factory = MakeFactory(emailMock, stytchApiBase: stytch.BaseAddress);
        using var client = factory.CreateClient();

        var body = JsonSerializer.Serialize(new
        {
            event_type = "direct.user.create",
            data = new { user = new { user_id = "user-live-001", emails = new[] { new { email = "newuser@example.com", verified = false } } } }
        });

        var response = await SignedPost(client, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            e => e.SendStytchVerificationAsync(
                "newuser@example.com",
                It.Is<string>(url => url.Contains("magic-token-abc")),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task Stytch_UserCreate_PreVerifiedEmail_DoesNotSendEmail()
    {
        var emailMock = new Mock<IEmailService>();
        var factory = MakeFactory(emailMock);
        using var client = factory.CreateClient();

        var body = JsonSerializer.Serialize(new
        {
            event_type = "direct.user.create",
            data = new { user = new { user_id = "user-social-001", emails = new[] { new { email = "social@example.com", verified = true } } } }
        });

        var response = await SignedPost(client, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            e => e.SendStytchVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Stytch_UnknownEventType_ReturnsOk_NoEmail()
    {
        var emailMock = new Mock<IEmailService>();
        var factory = MakeFactory(emailMock);
        using var client = factory.CreateClient();

        var body = JsonSerializer.Serialize(new
        {
            event_type = "session.authenticate",
            data = new { user = new { user_id = "user-x", emails = new[] { new { email = "x@y.z", verified = false } } } }
        });

        var response = await SignedPost(client, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            e => e.SendStytchVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Stytch_EmptyBody_Returns400()
    {
        var emailMock = new Mock<IEmailService>();
        var factory = MakeFactory(emailMock);
        using var client = factory.CreateClient();

        var response = await SignedPost(client, "");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stytch_InvalidSignature_Returns401()
    {
        var emailMock = new Mock<IEmailService>();
        var factory = MakeFactory(emailMock);
        using var client = factory.CreateClient();

        var body = JsonSerializer.Serialize(new
        {
            event_type = "direct.user.create",
            data = new { user = new { user_id = "user-forged", emails = new[] { new { email = "x@y.z", verified = false } } } }
        });

        // Sign with the WRONG secret → HMAC mismatch.
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var svixId = "msg_" + Guid.NewGuid().ToString("N");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var forgedSig = ComputeSvixSignature("whsec_" + "d3Jvbmctc2VjcmV0LWZvci1obWFjLXRlc3Q=", svixId, ts, body);
        content.Headers.Add("svix-id", svixId);
        content.Headers.Add("svix-timestamp", ts);
        content.Headers.Add("svix-signature", forgedSig);

        var response = await client.PostAsync("/api/webhooks/stytch/email", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        emailMock.Verify(
            e => e.SendStytchVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> MakeFactory(Mock<IEmailService> emailMock, Uri? stytchApiBase = null)
    {
        return Fixture.Factory.WithWebHostBuilder(b =>
        {
            // Known webhook secret so we can sign payloads; RequireSignature
            // stays true (Testing env is not Development).
            b.UseSetting("Stytch:WebhookSecret", TestSecret);
            if (stytchApiBase != null)
            {
                // Configure admin credentials + a stub base so MintMagicLink
                // actually issues an HTTP call (instead of short-circuiting).
                b.UseSetting("Stytch:ProjectId", "project-test-001");
                b.UseSetting("Stytch:Secret", "secret-test-001");
                b.UseSetting("Stytch:ApiBase", stytchApiBase.ToString());
            }
            b.ConfigureServices(s =>
            {
                Replace<IEmailService>(s, _ => emailMock.Object);
                // The settings singleton is captured at boot from config; the
                // UseSetting above feeds Program.cs's read of Stytch:WebhookSecret.
            });
        });
    }

    private async Task<HttpResponseMessage> SignedPost(HttpClient client, string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(body))
        {
            var svixId = "msg_" + Guid.NewGuid().ToString("N");
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var sig = ComputeSvixSignature(TestSecret, svixId, ts, body);
            content.Headers.Add("svix-id", svixId);
            content.Headers.Add("svix-timestamp", ts);
            content.Headers.Add("svix-signature", sig);
        }
        return await client.PostAsync("/api/webhooks/stytch/email", content);
    }

    private static string ComputeSvixSignature(string secret, string svixId, string svixTimestamp, string body)
    {
        var keyPart = secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret["whsec_".Length..] : secret;
        var keyBytes = Convert.FromBase64String(keyPart);
        var signedPayload = $"{svixId}.{svixTimestamp}.{body}";
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return "v1," + Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Minimal in-process HTTP stub for Stytch's admin API. Responds to
    /// POST /v1/magic_links with {"token": "..."} so the controller can
    /// build the magic-link URL and proceed to send the email.
    /// </summary>
    private static StytchAdminStub StubStytchAdmin(string token) => new(token);

    private sealed class StytchAdminStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        public Uri BaseAddress { get; }

        public StytchAdminStub(string token)
        {
            var port = GetFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            BaseAddress = new Uri(prefix);
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { break; }
                    var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { token }));
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(payload);
                    ctx.Response.Close();
                }
            });
        }

        private static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { /* ignore */ }
        }
    }

    private static void Replace<TService>(IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (existing != null) services.Remove(existing);
        services.AddSingleton(factory);
    }
}
