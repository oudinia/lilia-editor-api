using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Events.Common;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Wolverine;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="Lilia.Api.Controllers.WebhooksController"/>.
///
/// Behavior under test (post Kinde JWKS migration):
///   - user.created (signed JWT) → publishes <see cref="UserCreatedEvent"/>
///   - user.authenticated (signed JWT) → does NOT publish (sign-in, not registration)
///   - Missing email/id in payload → no publish, still 200
///   - Empty body → 400
///   - Invalid JWT signature → 401
///
/// JWT bodies are minted with a test RSA key and the matching public
/// key is fed into the controller via a mocked
/// <see cref="IKindeJwksProvider"/>. No HMAC, no shared secret —
/// Kinde signs every payload as a JWT.
/// </summary>
[Collection("Integration")]
public class WebhooksControllerTests : IntegrationTestBase
{
    private const string TestIssuer = "https://liliaeditor.kinde.com";

    public WebhooksControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Kinde_UserCreated_PublishesEvent()
    {
        var (factory, busMock, signing) = WithMocks();
        using var client = factory.CreateClient();

        var jwt = MintWebhookJwt(signing, new
        {
            type = "user.created",
            data = new { user = new { id = "kp_newuser_001", email = "newuser@example.com", first_name = "Sam" } }
        });

        var response = await client.PostAsync("/api/webhooks/kinde", new StringContent(jwt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(
                It.Is<UserCreatedEvent>(e =>
                    e.UserId == "kp_newuser_001" &&
                    e.Email == "newuser@example.com" &&
                    e.FirstName == "Sam"),
                It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Kinde_UserAuthenticated_DoesNotPublish()
    {
        var (factory, busMock, signing) = WithMocks();
        using var client = factory.CreateClient();

        var jwt = MintWebhookJwt(signing, new
        {
            type = "user.authenticated",
            data = new { user = new { id = "kp_returning_001", email = "returning@example.com" } }
        });

        var response = await client.PostAsync("/api/webhooks/kinde", new StringContent(jwt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_PayloadWithoutEmail_DoesNotPublish_StillReturns200()
    {
        var (factory, busMock, signing) = WithMocks();
        using var client = factory.CreateClient();

        var jwt = MintWebhookJwt(signing, new
        {
            type = "user.created",
            data = new { user = new { id = "kp_x", first_name = "Sam" } }
        });

        var response = await client.PostAsync("/api/webhooks/kinde", new StringContent(jwt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_PayloadWithoutUserId_DoesNotPublish_StillReturns200()
    {
        var (factory, busMock, signing) = WithMocks();
        using var client = factory.CreateClient();

        var jwt = MintWebhookJwt(signing, new
        {
            type = "user.created",
            data = new { user = new { email = "x@y.z", first_name = "Sam" } }
        });

        var response = await client.PostAsync("/api/webhooks/kinde", new StringContent(jwt));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_EmptyBody_Returns400()
    {
        var response = await Client.PostAsync("/api/webhooks/kinde", new StringContent(""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kinde_InvalidJwtSignature_Returns401_InProduction()
    {
        // Arrange: signing key the controller trusts is keyA; the token
        // is signed with keyB. Run the factory in Production env so the
        // dev fallback (treat-body-as-JSON) doesn't kick in.
        var trustedKey = MakeRsaKey();
        var attackerKey = MakeRsaKey();

        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()))
               .Returns(ValueTask.CompletedTask);

        var jwksMock = new Mock<IKindeJwksProvider>();
        jwksMock.Setup(j => j.GetSigningKeysAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { trustedKey });

        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureServices(s =>
            {
                Replace<IMessageBus>(s, _ => busMock.Object);
                Replace<IKindeJwksProvider>(s, _ => jwksMock.Object);
            });
        });
        using var client = factory.CreateClient();

        var forged = MintWebhookJwt(attackerKey, new { type = "user.created", data = new { user = new { id = "kp_forged", email = "x@y.z" } } });
        var response = await client.PostAsync("/api/webhooks/kinde", new StringContent(forged));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        busMock.Verify(x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()), Times.Never);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private (WebApplicationFactory<Program> factory, Mock<IMessageBus> bus, RsaSecurityKey signing)
        WithMocks()
    {
        var signing = MakeRsaKey();

        var busMock = new Mock<IMessageBus>();
        busMock.Setup(b => b.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()))
               .Returns(ValueTask.CompletedTask);

        var jwksMock = new Mock<IKindeJwksProvider>();
        jwksMock.Setup(j => j.GetSigningKeysAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { signing });

        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:Authority", TestIssuer);
            b.ConfigureServices(s =>
            {
                Replace<IMessageBus>(s, _ => busMock.Object);
                Replace<IKindeJwksProvider>(s, _ => jwksMock.Object);
            });
        });
        return (factory, busMock, signing);
    }

    private static RsaSecurityKey MakeRsaKey()
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa) { KeyId = "test-kid-" + Guid.NewGuid().ToString("N")[..8] };
    }

    private static string MintWebhookJwt(SecurityKey key, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        // Seed the standard JWT claims Kinde would set (iss, iat) plus
        // every property of the test payload as a top-level claim — that
        // way the controller's JsonDocument.Parse sees the same shape.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var token = handler.CreateJwtSecurityToken(
            issuer: TestIssuer,
            audience: null,
            subject: null,
            notBefore: DateTime.UtcNow.AddSeconds(-30),
            expires: DateTime.UtcNow.AddMinutes(5),
            issuedAt: DateTime.UtcNow,
            signingCredentials: creds);

        // Inject the user-supplied payload claims directly (object
        // values like `data.user` round-trip as nested JSON).
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            token.Payload[prop.Name] = JsonValueToClr(prop.Value);
        }
        return handler.WriteToken(token);
    }

    private static object? JsonValueToClr(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonValueToClr(p.Value)),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonValueToClr).ToList(),
        _ => null,
    };

    private static void Replace<TService>(IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (existing != null) services.Remove(existing);
        services.AddSingleton(factory);
    }
}
