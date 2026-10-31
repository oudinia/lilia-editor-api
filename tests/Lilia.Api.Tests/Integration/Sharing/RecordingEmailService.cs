using System.Collections.Concurrent;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Integration.Sharing;

/// <summary>Every email the API would send, by kind and recipient, sent nowhere.</summary>
public sealed class RecordingEmailService : IEmailService
{
    public sealed record Sent(string Kind, string To, string Detail);

    public ConcurrentQueue<Sent> Outbox { get; } = new();

    private Task Record(string kind, string to, string detail = "")
    {
        Outbox.Enqueue(new Sent(kind, to, detail));
        return Task.CompletedTask;
    }

    public void Clear() => Outbox.Clear();

    public IReadOnlyList<Sent> To(string email) => Outbox.Where(s => string.Equals(s.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendDocumentInviteAsync(string toEmail, EmailActor inviter, string documentTitle, string role, string documentUrl, string? locale) => Record("invite", toEmail, documentUrl);
    public Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null) => Record("raw", to, subject);
    public Task SendWelcomeAsync(string toEmail, string? firstName, string? locale) => Record("welcome", toEmail, locale ?? "");
    public Task SendGettingStartedAsync(string toEmail, string? firstName, string? locale, string tipsUrl) => Record("getting-started", toEmail, tipsUrl);
    public Task SendTeamWelcomeAsync(string toEmail, string teamName, string teamCodename, string teamUrl, string? locale) => Record("team-welcome", toEmail);
    public Task SendDocumentSharedAsync(string toEmail, EmailActor inviter, string documentTitle, string role, string documentUrl, string? locale) => Record("shared", toEmail, documentUrl);
    public Task SendTeamInviteAsync(string toEmail, EmailActor inviter, string teamName, string role, string acceptUrl, string? locale) => Record("team-invite", toEmail);
    public Task SendTeamMemberRemovedAsync(string toEmail, EmailActor removedBy, string teamName, string? locale) => Record("team-removed", toEmail);
    public Task SendStytchVerificationAsync(string toEmail, string magicLinkUrl, string? locale) => Record("verify", toEmail);
    public Task SendStytchPasswordResetAsync(string toEmail, string resetUrl, string? locale) => Record("reset", toEmail);
    public Task SendStytchMagicLinkLoginAsync(string toEmail, string magicLinkUrl, string? locale) => Record("magic-link", toEmail);
    public Task SendTwoStepCodeAsync(string toEmail, string code, string? locale) => Record("two-step-code", toEmail, code);
    public Task SendInviteAcceptedAsync(string toEmail, EmailActor joined, string documentTitle, string? role, string sharingUrl, string? locale) => Record("accepted", toEmail, sharingUrl);
    public Task SendAccessChangedAsync(string toEmail, EmailActor changedBy, string documentTitle, string oldRole, string newRole, string documentUrl, string? locale) => Record("role-changed", toEmail, $"{EmailService.RoleLine(oldRole, locale)} → {EmailService.RoleLine(newRole, locale)}");
    public Task SendAccessRemovedAsync(string toEmail, EmailActor removedBy, string documentTitle, bool allowReply, string? locale) => Record("removed", toEmail, allowReply ? removedBy.Email ?? "" : "");
    public Task SendExportReadyAsync(string toEmail, string? locale, DateTime requestedAt, DateTime expiresAt, string fileName, int documents, long bytes) => Record("export", toEmail, fileName);
    public Task SendAccountClosedAsync(string toEmail, string? locale, DateTime closedAt, DateTime eraseOn) => Record("closed", toEmail);
    public Task SendAccountErasedAsync(string toEmail, string? locale, DateTime erasedAt) => Record("erased", toEmail);
}
