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

    public IReadOnlyList<Sent> To(string email) => Outbox.Where(s => string.Equals(s.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendDocumentInviteAsync(string toEmail, string inviterName, string documentTitle, string role, string documentUrl) => Record("invite", toEmail, documentUrl);
    public Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null) => Record("raw", to, subject);
    public Task SendWelcomeAsync(string toEmail, string? firstName) => Record("welcome", toEmail);
    public Task SendTeamWelcomeAsync(string toEmail, string? firstName, string teamCodename) => Record("team-welcome", toEmail);
    public Task SendDocumentSharedAsync(string toEmail, string? recipientName, string inviterName, string documentTitle, string permission, string documentUrl) => Record("shared", toEmail, documentUrl);
    public Task SendTeamInviteAsync(string toEmail, string inviterName, string teamName, string role, string acceptUrl) => Record("team-invite", toEmail);
    public Task SendTeamMemberRemovedAsync(string toEmail, string? recipientName, string removerName, string teamName) => Record("team-removed", toEmail);
    public Task SendStytchVerificationAsync(string toEmail, string magicLinkUrl, string? locale) => Record("verify", toEmail);
    public Task SendStytchPasswordResetAsync(string toEmail, string resetUrl, string? locale) => Record("reset", toEmail);
    public Task SendStytchMagicLinkLoginAsync(string toEmail, string magicLinkUrl, string? locale) => Record("magic-link", toEmail);
    public Task SendInviteAcceptedAsync(string toEmail, string acceptedByName, string documentTitle, string sharingUrl) => Record("accepted", toEmail, sharingUrl);
    public Task SendAccessChangedAsync(string toEmail, string changedByName, string documentTitle, string oldRole, string newRole, string documentUrl) => Record("role-changed", toEmail, $"{oldRole} → {newRole}");
    public Task SendAccessRemovedAsync(string toEmail, string removedByName, string documentTitle) => Record("removed", toEmail);
}
