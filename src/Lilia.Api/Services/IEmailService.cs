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

    /// <summary>
    /// Stytch BYOM — email verification for first-time sign-up. Fired
    /// from StytchWebhookController when Stytch posts a
    /// <c>magic_links.email.send</c> or <c>magic_links.email.login_or_create</c>
    /// event for an unverified address. The magic-link URL already has
    /// the Stytch token embedded; clicking it lands on
    /// <c>/auth/callback</c> which marks the email verified.
    /// </summary>
    Task SendStytchVerificationAsync(string toEmail, string magicLinkUrl, string? locale);

    /// <summary>
    /// Stytch BYOM — password reset. Fired when Stytch posts a
    /// <c>passwords.email.reset.start</c> event. The reset URL has the
    /// reset token embedded; clicking lands on <c>/reset-password</c>.
    /// </summary>
    Task SendStytchPasswordResetAsync(string toEmail, string resetUrl, string? locale);

    /// <summary>
    /// Stytch BYOM — magic-link sign-in (the "quiet third option" on
    /// the sign-in form). Fired for returning users who chose magic
    /// link over password. URL lands on <c>/auth/callback</c>.
    /// </summary>
    Task SendStytchMagicLinkLoginAsync(string toEmail, string magicLinkUrl, string? locale);
}
