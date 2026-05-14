using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Lilia.Api.Events.Common;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wolverine;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="Lilia.Api.Controllers.WebhooksController"/>.
///
/// Behavior under test (post Wolverine-slice migration):
///   - user.created → publishes <see cref="UserCreatedEvent"/> on the bus
///   - user.authenticated → does NOT publish (sign-in, not registration)
///   - Missing email/id in payload → no publish, still 200
///   - Invalid JSON → 400
///   - With Webhooks:Kinde:Secret set:
///       missing X-Kinde-Signature → 401
///       valid HMAC signature → 200, event published
///       valid signature with sha256= prefix → 200
///       invalid signature → 401
///
/// Email dispatch is no longer the webhook's job — that lives in
/// CreateDefaultTeamHandler in the Teams slice. Tests for that handler
/// belong in a sibling file once it has dedicated coverage.
/// </summary>
[Collection("Integration")]
public class WebhooksControllerTests : IntegrationTestBase
{
    public WebhooksControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string KindeUserCreatedPayload = """
        {
          "type": "user.created",
          "data": {
            "user": {
              "id": "kp_newuser_001",
              "email": "newuser@example.com",
              "first_name": "Sam"
            }
          }
        }
        """;

    private const string KindeUserAuthenticatedPayload = """
        {
          "type": "user.authenticated",
          "data": {
            "user": {
              "id": "kp_returning_001",
              "email": "returning@example.com",
              "first_name": "Sam"
            }
          }
        }
        """;

    [Fact]
    public async Task Kinde_UserCreated_PublishesEvent()
    {
        var (factory, busMock) = WithMockBus();
        using var client = factory.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

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
        var (factory, busMock) = WithMockBus();
        using var client = factory.CreateClient();

        var content = new StringContent(KindeUserAuthenticatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_PayloadWithoutEmail_DoesNotPublish_StillReturns200()
    {
        var (factory, busMock) = WithMockBus();
        using var client = factory.CreateClient();

        var noEmail = """{ "type": "user.created", "data": { "user": { "id": "kp_x", "first_name": "Sam" } } }""";
        var content = new StringContent(noEmail, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_PayloadWithoutUserId_DoesNotPublish_StillReturns200()
    {
        // The webhook now requires both email AND user-id to publish — without
        // an id we can't seed the User row, so we'd rather skip than emit a
        // half-formed event. Webhook still returns 200 so Kinde doesn't retry.
        var (factory, busMock) = WithMockBus();
        using var client = factory.CreateClient();

        var noId = """{ "type": "user.created", "data": { "user": { "email": "x@y.z", "first_name": "Sam" } } }""";
        var content = new StringContent(noId, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_InvalidJson_Returns400()
    {
        var content = new StringContent("not-valid-json{[", Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kinde_WithSecret_MissingSignature_Returns401()
    {
        const string secret = "test-secret-1234567890";
        var (factory, busMock) = WithMockBus(secret);
        using var client = factory.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_WithSecret_ValidSignature_Returns200_AndPublishes()
    {
        const string secret = "test-secret-1234567890";
        var (factory, busMock) = WithMockBus(secret);
        using var client = factory.CreateClient();

        var signature = ComputeHmacHex(KindeUserCreatedPayload, secret);
        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Kinde-Signature", signature);

        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(
                It.Is<UserCreatedEvent>(e => e.UserId == "kp_newuser_001"),
                It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Kinde_WithSecret_ValidSignature_Sha256Prefixed_Returns200()
    {
        const string secret = "test-secret-1234567890";
        var (factory, busMock) = WithMockBus(secret);
        using var client = factory.CreateClient();

        var signature = "sha256=" + ComputeHmacHex(KindeUserCreatedPayload, secret);
        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Kinde-Signature", signature);

        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Kinde_WithSecret_InvalidSignature_Returns401()
    {
        const string secret = "test-secret-1234567890";
        var (factory, busMock) = WithMockBus(secret);
        using var client = factory.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Kinde-Signature", "deadbeef-not-a-valid-signature");

        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        busMock.Verify(
            x => x.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    /// <summary>
    /// Swap the registered <see cref="IMessageBus"/> for a Moq so we can
    /// observe what the webhook publishes without spinning up Wolverine's
    /// runtime. Optionally sets the Kinde HMAC secret.
    /// </summary>
    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, Mock<IMessageBus> bus)
        WithMockBus(string? hmacSecret = null)
    {
        var busMock = new Mock<IMessageBus>();
        busMock
            .Setup(b => b.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<DeliveryOptions>()))
            .Returns(ValueTask.CompletedTask);

        var factory = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            if (hmacSecret is not null) builder.UseSetting("Webhooks:Kinde:Secret", hmacSecret);
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IMessageBus));
                if (existing != null) services.Remove(existing);
                services.AddScoped(_ => busMock.Object);
            });
        });
        return (factory, busMock);
    }

    private static string ComputeHmacHex(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
