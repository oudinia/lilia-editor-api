namespace Lilia.Api.Services;

public interface IEmailService
{
    Task SendDocumentInviteAsync(string toEmail, string inviterName, string documentTitle, string role, string documentUrl);
    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null);
    /// <summary>Welcome email triggered by Kinde user.created (LILIA-95).</summary>
    Task SendWelcomeAsync(string toEmail, string? firstName);
}
