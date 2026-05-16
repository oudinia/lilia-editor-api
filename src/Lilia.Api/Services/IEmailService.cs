namespace Lilia.Api.Services;

public interface IEmailService
{
    Task SendDocumentInviteAsync(string toEmail, string inviterName, string documentTitle, string role, string documentUrl);
    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null);
    /// <summary>Welcome email triggered by Kinde user.created (LILIA-95).</summary>
    Task SendWelcomeAsync(string toEmail, string? firstName);
    /// <summary>
    /// New default team announcement — sent on user.created right after
    /// the welcome (or as a standalone test from /api/teams/test-welcome).
    /// </summary>
    Task SendTeamWelcomeAsync(string toEmail, string? firstName, string teamCodename);

    /// <summary>
    /// Doc-share notification for the by-id path (someone added you to a
    /// document via user-search). Different from <c>SendDocumentInviteAsync</c>
    /// which fires from the email-invite path with a sign-up URL.
    /// </summary>
    Task SendDocumentSharedAsync(string toEmail, string? recipientName, string inviterName,
        string documentTitle, string permission, string documentUrl);

    /// <summary>
    /// Team-invite email — "X invited you to <team-codename>". Fired from
    /// the Wolverine handler that subscribes to TeamInviteCreatedEvent.
    /// </summary>
    Task SendTeamInviteAsync(string toEmail, string inviterName, string teamName,
        string role, string acceptUrl);

    /// <summary>
    /// Removal notice — "You've been removed from <team-codename>".
    /// Fired from the Wolverine handler subscribing to
    /// TeamMemberRemovedEvent. Mirror of SendTeamInviteAsync.
    /// </summary>
    Task SendTeamMemberRemovedAsync(string toEmail, string? recipientName,
        string removerName, string teamName);
}
