namespace Lilia.Api.Services;

/// <summary>Who did it, as the sharing and team emails show them: a name, an address, and initials from the name.</summary>
public sealed record EmailActor(string Name, string? Email);

public interface IEmailService
{
    /// <summary>To someone without an account: an invitation to a document (sign-up, then the document).</summary>
    Task SendDocumentInviteAsync(string toEmail, EmailActor inviter, string documentTitle, string role, string documentUrl, string? locale);
    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null);
    /// <summary>
    /// An account email (export ready, account closed…): routed as <c>EmailKind.Account</c>, so it
    /// never carries an Unsubscribe link. Test doubles that don't care get the plain send.
    /// </summary>
    Task SendAccountEmailAsync(string to, string subject, string htmlBody, string? textBody = null) =>
        SendEmailAsync(to, subject, htmlBody, textBody);
    /// <summary>Welcome to Lilia (17 Oct design), in the person's language: once per account (OnboardingService).</summary>
    Task SendWelcomeAsync(string toEmail, string? firstName, string? locale);
    /// <summary>The day-3 tip to someone who hasn't started a document; <paramref name="tipsUrl"/> turns tips off in one click.</summary>
    Task SendGettingStartedAsync(string toEmail, string? firstName, string? locale, string tipsUrl);
    /// <summary>To someone who created a team: it is ready, with its code.</summary>
    Task SendTeamWelcomeAsync(string toEmail, string teamName, string teamCodename, string teamUrl, string? locale);

    /// <summary>
    /// Doc-share notification for the by-id path (someone added you to a
    /// document via user-search). Different from <c>SendDocumentInviteAsync</c>
    /// which fires from the email-invite path with a sign-up URL.
    /// </summary>
    Task SendDocumentSharedAsync(string toEmail, EmailActor inviter, string documentTitle, string role, string documentUrl, string? locale);

    /// <summary>
    /// Team-invite email — "X invited you to <team-codename>". Fired from
    /// the Wolverine handler that subscribes to TeamInviteCreatedEvent.
    /// </summary>
    Task SendTeamInviteAsync(string toEmail, EmailActor inviter, string teamName, string role, string acceptUrl, string? locale);

    /// <summary>
    /// Removal notice — "You've been removed from <team-codename>".
    /// Fired from the Wolverine handler subscribing to
    /// TeamMemberRemovedEvent. Mirror of SendTeamInviteAsync.
    /// </summary>
    Task SendTeamMemberRemovedAsync(string toEmail, EmailActor removedBy, string teamName, string? locale);

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

    /// <summary>A two-step sign-in code (25 Oct 2026): six digits, valid 10 minutes, in the account's language.</summary>
    Task SendTwoStepCodeAsync(string toEmail, string code, string? locale);

    /// <summary>To the owner: someone accepted their invitation ("Open sharing").</summary>
    Task SendInviteAcceptedAsync(string toEmail, EmailActor joined, string documentTitle, string? role, string sharingUrl, string? locale);

    /// <summary>To a collaborator: their access changed, old and new ("Open document").</summary>
    Task SendAccessChangedAsync(string toEmail, EmailActor changedBy, string documentTitle, string oldRole, string newRole, string documentUrl, string? locale);

    /// <summary>To a former collaborator: their access was removed (no button). Replies reach the owner when they allow it.</summary>
    Task SendAccessRemovedAsync(string toEmail, EmailActor removedBy, string documentTitle, bool allowReply, string? locale);

    /// <summary>The account export is ready to download (Settings → Your data), until <paramref name="expiresAt"/>.</summary>
    Task SendExportReadyAsync(string toEmail, string? locale, DateTime requestedAt, DateTime expiresAt, string fileName, int documents, long bytes);

    /// <summary>The account is closed; it can be restored until <paramref name="eraseOn"/>.</summary>
    Task SendAccountClosedAsync(string toEmail, string? locale, DateTime closedAt, DateTime eraseOn);

    /// <summary>The account and its documents are erased.</summary>
    Task SendAccountErasedAsync(string toEmail, string? locale, DateTime erasedAt);
}
