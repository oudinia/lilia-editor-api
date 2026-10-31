using System.Net;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Services.Email;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The email gateway (the user, 17 Oct: "a gateway to switch provider easily"): each kind of email
/// has a route of providers tried in order; account emails can avoid the provider that adds an
/// Unsubscribe link, and fall back to it rather than not arrive.
/// </summary>
public class EmailGatewayTests
{
    private sealed class FakeProvider(string name, bool configured = true, bool fails = false) : IEmailProvider
    {
        public List<OutgoingEmail> Sent { get; } = [];
        public string Name => name;
        public bool IsConfigured => configured;
        public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
        {
            if (fails) throw new InvalidOperationException($"{name} is down");
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private static OutgoingEmail Mail(EmailKind kind) => new("ada@lilia.test", "Subject", "<p>x</p>", "x", null, kind);

    private static EmailGateway Gateway(EmailSettings s, params IEmailProvider[] providers) =>
        new(providers, s, NullLogger<EmailGateway>.Instance);

    [Fact]
    public void Without_routes_every_kind_uses_the_transport_as_before()
    {
        var g = Gateway(new EmailSettings { Transport = "smtp" });
        g.Route(EmailKind.Account).Should().Equal("smtp");
        g.Route(EmailKind.Notification).Should().Equal("smtp");
    }

    [Fact]
    public async Task Account_and_notification_emails_take_their_own_routes()
    {
        var resend = new FakeProvider("resend");
        var smtp = new FakeProvider("smtp");
        var g = Gateway(new EmailSettings { Transport = "smtp", AccountRoute = "resend, smtp", NotificationRoute = "smtp" }, resend, smtp);

        (await g.SendAsync(Mail(EmailKind.Account))).Should().Be("resend");
        (await g.SendAsync(Mail(EmailKind.Notification))).Should().Be("smtp");
        resend.Sent.Should().ContainSingle().Which.Kind.Should().Be(EmailKind.Account);
        smtp.Sent.Should().ContainSingle().Which.Kind.Should().Be(EmailKind.Notification);
    }

    [Fact]
    public async Task When_the_first_provider_fails_the_next_one_sends_it()
    {
        var resend = new FakeProvider("resend", fails: true);
        var smtp = new FakeProvider("smtp");
        var g = Gateway(new EmailSettings { AccountRoute = "resend,smtp" }, resend, smtp);
        (await g.SendAsync(Mail(EmailKind.Account))).Should().Be("smtp");
        smtp.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task A_provider_without_settings_is_skipped_not_fatal()
    {
        var resend = new FakeProvider("resend", configured: false);
        var smtp = new FakeProvider("smtp");
        var g = Gateway(new EmailSettings { AccountRoute = "resend,smtp" }, resend, smtp);
        (await g.SendAsync(Mail(EmailKind.Account))).Should().Be("smtp");
    }

    [Fact]
    public async Task When_every_provider_fails_the_caller_hears_about_it()
    {
        var g = Gateway(new EmailSettings { AccountRoute = "resend,smtp,unknown" },
            new FakeProvider("resend", fails: true), new FakeProvider("smtp", fails: true));
        var act = () => g.SendAsync(Mail(EmailKind.Account));
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("resend,smtp,unknown");
    }

    [Fact]
    public async Task Sign_in_links_and_exports_are_account_emails_sharing_is_a_notification()
    {
        var resend = new FakeProvider("resend");
        var smtp = new FakeProvider("smtp");
        var settings = new EmailSettings { AccountRoute = "resend", NotificationRoute = "smtp" };
        var service = new EmailService(settings, NullLogger<EmailService>.Instance, Gateway(settings, resend, smtp));

        await service.SendStytchMagicLinkLoginAsync("ada@lilia.test", "https://editor.liliaeditor.com/auth/callback?token=t", "en");
        await service.SendStytchPasswordResetAsync("ada@lilia.test", "https://editor.liliaeditor.com/reset-password?token=t", "fr");
        await service.SendAccountEmailAsync("ada@lilia.test", "Your Lilia export is ready", "<p>ready</p>");
        await service.SendAccessChangedAsync("ada@lilia.test", new EmailActor("Nadia", "nadia@lilia.test"), "Thesis", "viewer", "editor", "https://editor.liliaeditor.com/d/1", "en");

        resend.Sent.Select(e => e.Kind).Should().AllBeEquivalentTo(EmailKind.Account).And.HaveCount(3);
        smtp.Sent.Should().ContainSingle().Which.Subject.Should().Contain("Thesis");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"x\"}") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    [Fact]
    public async Task Resend_gets_the_email_with_the_key_and_no_unsubscribe_header()
    {
        var stub = new StubHandler();
        var p = new ResendEmailProvider(new EmailSettings { ResendApiKey = "re_test", FromName = "Lilia", FromAddress = "noreply@liliaeditor.com" }, new Factory(stub));
        await p.SendAsync(new OutgoingEmail("ada@lilia.test", "Your Lilia sign-in link", "<p>x</p>", "x", "owner@lilia.test", EmailKind.Account));

        stub.Request!.RequestUri!.ToString().Should().Be("https://api.resend.com/emails");
        stub.Request.Headers.Authorization!.ToString().Should().Be("Bearer re_test");
        stub.Body.Should().Contain("\"from\":\"Lilia \\u003Cnoreply@liliaeditor.com\\u003E\"")
            .And.Contain("\"reply_to\":\"owner@lilia.test\"")
            .And.NotContain("List-Unsubscribe").And.NotContain("headers");
    }

    [Fact]
    public void Resend_without_a_key_is_not_configured()
    {
        new ResendEmailProvider(new EmailSettings(), new Factory(new StubHandler())).IsConfigured.Should().BeFalse();
    }
}
