namespace Lilia.Api.Services;

public interface IEmailService
{
    Task SendDocumentInviteAsync(string toEmail, string inviterName, string documentTitle, string role, string documentUrl);
    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null);
}
