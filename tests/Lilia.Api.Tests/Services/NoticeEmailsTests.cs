using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The sharing, team and account emails in the 17 Oct design (31 Oct): sent through the service,
/// in each language, with nothing left unfilled.
/// </summary>
public class NoticeEmailsTests
{
    private sealed class Outbox : IEmailProvider
    {
        public List<OutgoingEmail> Sent { get; } = [];
        public string Name => "smtp";
        public bool IsConfigured => true;
        public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private static (EmailService Service, Outbox Outbox) Make()
    {
        var settings = new EmailSettings { Transport = "smtp" };
        var outbox = new Outbox();
        return (new EmailService(settings, NullLogger<EmailService>.Instance,
            new EmailGateway([outbox], settings, NullLogger<EmailGateway>.Instance)), outbox);
    }

    private static readonly EmailActor Ana = new("Ana Ruiz", "ana@lilia.test");
    private const string Doc = "https://editor.liliaeditor.com/document/1";

    private static async Task SendAll(EmailService s, string lang)
    {
        await s.SendDocumentInviteAsync("ben@lilia.test", Ana, "Field notes", "editor", "https://editor.liliaeditor.com/invites/t", lang);
        await s.SendDocumentSharedAsync("ben@lilia.test", Ana, "Field notes", "viewer", Doc, lang);
        await s.SendInviteAcceptedAsync("ana@lilia.test", new EmailActor("Ben Okafor", "ben@lilia.test"), "Field notes", "editor", Doc, lang);
        await s.SendAccessChangedAsync("ben@lilia.test", Ana, "Field notes", "viewer", "editor", Doc, lang);
        await s.SendAccessRemovedAsync("ben@lilia.test", Ana, "Field notes", allowReply: true, lang);
        await s.SendTeamInviteAsync("ben@lilia.test", Ana, "Quality lab", "editor", "https://editor.liliaeditor.com/teams/1", lang);
        await s.SendTeamWelcomeAsync("ana@lilia.test", "Quality lab", "quiet-heron", "https://editor.liliaeditor.com/teams", lang);
        await s.SendTeamMemberRemovedAsync("ben@lilia.test", Ana, "Quality lab", lang);
        await s.SendExportReadyAsync("ana@lilia.test", lang, new DateTime(2026, 10, 31), new DateTime(2026, 11, 7), "lilia-export-2026-10-31.zip", 12, 3_500_000);
        await s.SendAccountClosedAsync("ana@lilia.test", lang, new DateTime(2026, 10, 31), new DateTime(2026, 11, 30));
        await s.SendAccountErasedAsync("ana@lilia.test", lang, new DateTime(2026, 11, 30));
    }

    [Theory]
    [InlineData("en"), InlineData("fr"), InlineData("es")]
    public async Task Every_email_is_filled_in_every_language(string lang)
    {
        var (service, outbox) = Make();
        await SendAll(service, lang);

        outbox.Sent.Should().HaveCount(11);
        foreach (var email in outbox.Sent)
        {
            email.Subject.Should().NotContain("{{").And.NotBeNullOrWhiteSpace();
            email.Html.Should().NotContain("{{", email.Subject);
            email.Text.Should().NotContain("{{", email.Subject);
            email.Html.Should().NotContain("settings/notifications", "there is no such settings page");
        }
    }

    [Fact]
    public async Task The_reader_gets_names_roles_and_dates_in_their_language()
    {
        var (service, outbox) = Make();
        await SendAll(service, "fr");

        var shared = outbox.Sent[1];
        shared.Subject.Should().Contain("Ana Ruiz").And.Contain("Field notes");
        shared.Text.Should().Contain("Vous pouvez lire").And.Contain("ana@lilia.test");
        shared.Html.Should().Contain(">AR<", "the disc shows the sharer's initials");

        outbox.Sent[2].Text.Should().Contain("Peut modifier", "the owner reads the role of who joined");
        outbox.Sent[3].Text.Should().Contain("Vous pouvez modifier").And.Contain("Vous pouviez lire");
        outbox.Sent[8].Text.Should().Contain("31 octobre 2026").And.Contain("7 novembre 2026").And.Contain("12 documents · 3 Mo");
        outbox.Sent[9].Text.Should().Contain("30 novembre 2026");
        outbox.Sent[10].Subject.Should().Be("Votre compte Lilia est effacé");
    }

    [Fact]
    public async Task Replies_reach_the_owner_only_when_they_allow_it()
    {
        var (service, outbox) = Make();
        await service.SendAccessRemovedAsync("ben@lilia.test", Ana, "Field notes", allowReply: true, "en");
        await service.SendAccessRemovedAsync("ben@lilia.test", Ana, "Field notes", allowReply: false, "en");

        outbox.Sent[0].ReplyTo.Should().Be("ana@lilia.test");
        outbox.Sent[0].Text.Should().Contain("reply to this email");
        outbox.Sent[1].ReplyTo.Should().BeNull();
        outbox.Sent[1].Text.Should().NotContain("reply to this email");
    }

    [Fact]
    public async Task Sharing_and_teams_are_notifications_the_account_ones_are_account_emails()
    {
        var (service, outbox) = Make();
        await SendAll(service, "en");
        outbox.Sent.Take(8).Should().OnlyContain(e => e.Kind == EmailKind.Notification);
        outbox.Sent.Skip(8).Should().OnlyContain(e => e.Kind == EmailKind.Account);
    }

    [Theory]
    [InlineData("editor", "en", "Can edit"), InlineData("member", "es", "Puede editar"), InlineData("viewer", "fr", "Peut lire"),
     InlineData("owner", "de", "Owner")]
    public void Roles_read_as_the_share_drawer_says_them(string role, string lang, string expected) =>
        EmailService.RoleLine(role, lang).Should().Be(expected);

    [Theory]
    [InlineData("editor", "en", false, "You can edit"), InlineData("viewer", "fr", true, "Vous pouviez lire"),
     InlineData("member", "es", false, "Puedes editar"), InlineData("owner", "en", true, "You were the owner")]
    public void The_reader_role_is_said_to_them(string role, string lang, bool past, string expected) =>
        EmailService.YourRole(role, lang, past).Should().Be(expected);

    [Theory]
    [InlineData("Nadia Benali", 2, "NB"), InlineData("ana@example.org", 2, "A"), InlineData("Quality lab", 1, "Q"), InlineData("", 2, "?")]
    public void Initials_come_from_the_name(string name, int max, string expected) =>
        EmailService.Initials(name, max).Should().Be(expected);

    [Fact]
    public void Dates_are_written_in_the_email_language()
    {
        var day = new DateTime(2026, 10, 31);
        EmailTemplates.Date(day, "en").Should().Be("31 October 2026");
        EmailTemplates.Date(day, "fr-FR").Should().Be("31 octobre 2026");
        EmailTemplates.Date(day, "es").Should().Be("31 de octubre de 2026");
    }
}
