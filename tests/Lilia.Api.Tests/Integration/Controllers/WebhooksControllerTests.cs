using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="Lilia.Api.Controllers.WebhooksController"/>.
///
/// What we cover:
///   - user.created → welcome email dispatched
///   - user.authenticated → NO welcome email (sign-in, not registration)
///   - Missing email in payload → no dispatch + 200 OK
///   - Invalid JSON → 400
///   - With Webhooks:Kinde:Secret set, missing X-Kinde-Signature → 401
///   - With Webhooks:Kinde:Secret set, valid HMAC signature → 200
///   - With Webhooks:Kinde:Secret set, valid signature with "sha256=" prefix → 200
///   - With Webhooks:Kinde:Secret set, invalid signature → 401
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
              "email": "returning@example.com",
              "first_name": "Sam"
            }
          }
        }
        """;

    [Fact]
    public async Task Kinde_UserCreated_DispatchesWelcomeEmail()
    {
        // Mock IEmailService → assert SendWelcomeAsync called once.
        var emailMock = new Mock<IEmailService>();
        var factoryWithMockEmail = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithMockEmail.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            x => x.SendWelcomeAsync("newuser@example.com", "Sam"),
            Times.Once);
    }

    [Fact]
    public async Task Kinde_UserAuthenticated_DoesNotDispatchWelcomeEmail()
    {
        var emailMock = new Mock<IEmailService>();
        var factoryWithMockEmail = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithMockEmail.CreateClient();

        var content = new StringContent(KindeUserAuthenticatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            x => x.SendWelcomeAsync(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_PayloadWithoutEmail_DoesNotDispatch_StillReturns200()
    {
        var emailMock = new Mock<IEmailService>();
        var factoryWithMockEmail = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithMockEmail.CreateClient();

        var noEmail = """{ "type": "user.created", "data": { "user": { "first_name": "Sam" } } }""";
        var content = new StringContent(noEmail, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            x => x.SendWelcomeAsync(It.IsAny<string>(), It.IsAny<string?>()),
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
        var emailMock = new Mock<IEmailService>();
        var factoryWithSecret = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Webhooks:Kinde:Secret", secret);
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithSecret.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        emailMock.Verify(
            x => x.SendWelcomeAsync(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_WithSecret_ValidSignature_Returns200_AndDispatches()
    {
        const string secret = "test-secret-1234567890";
        var emailMock = new Mock<IEmailService>();
        var factoryWithSecret = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Webhooks:Kinde:Secret", secret);
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithSecret.CreateClient();

        var signature = ComputeHmacHex(KindeUserCreatedPayload, secret);
        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Kinde-Signature", signature);

        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            x => x.SendWelcomeAsync("newuser@example.com", "Sam"),
            Times.Once);
    }

    [Fact]
    public async Task Kinde_WithSecret_ValidSignature_Sha256Prefixed_Returns200()
    {
        const string secret = "test-secret-1234567890";
        var emailMock = new Mock<IEmailService>();
        var factoryWithSecret = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Webhooks:Kinde:Secret", secret);
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithSecret.CreateClient();

        var signature = "sha256=" + ComputeHmacHex(KindeUserCreatedPayload, secret);
        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Kinde-Signature", signature);

        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            x => x.SendWelcomeAsync("newuser@example.com", "Sam"),
            Times.Once);
    }

    [Fact]
    public async Task Kinde_WithSecret_InvalidSignature_Returns401()
    {
        const string secret = "test-secret-1234567890";
        var emailMock = new Mock<IEmailService>();
        var factoryWithSecret = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Webhooks:Kinde:Secret", secret);
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithSecret.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Kinde-Signature", "deadbeef-not-a-valid-signature");

        var response = await client.PostAsync("/api/webhooks/kinde", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        emailMock.Verify(
            x => x.SendWelcomeAsync(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Kinde_EmailFailure_DoesNotPropagate_Returns200()
    {
        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(x => x.SendWelcomeAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("Resend down"));

        var factoryWithFailingEmail = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailService));
                if (existing != null) services.Remove(existing);
                services.AddSingleton(emailMock.Object);
            });
        });
        using var client = factoryWithFailingEmail.CreateClient();

        var content = new StringContent(KindeUserCreatedPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/kinde", content);

        // Webhook deliberately swallows email failures so Kinde doesn't retry-loop.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailMock.Verify(
            x => x.SendWelcomeAsync("newuser@example.com", "Sam"),
            Times.Once);
    }

    private static string ComputeHmacHex(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
