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
}
