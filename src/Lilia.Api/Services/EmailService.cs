using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Lilia.Api.Services;

public class EmailSettings
{
    public string ResendApiKey { get; set; } = "";
    public string FromAddress { get; set; } = "noreply@liliaeditor.com";
    public string FromName { get; set; } = "Lilia";
    public string BaseUrl { get; set; } = "https://editor.liliaeditor.com";
}

public class EmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;
    private readonly IStringLocalizer<EmailService> _localizer;

    public EmailService(EmailSettings settings, ILogger<EmailService> logger, IStringLocalizer<EmailService> localizer)
    {
        _settings = settings;
        _logger = logger;
        _localizer = localizer;
        _http = new HttpClient { BaseAddress = new Uri("https://api.resend.com") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
    }

    public async Task SendDocumentInviteAsync(string toEmail, string inviterName, string documentTitle, string role, string documentUrl)
    {
        var subject = string.Format(_localizer["InviteSubject"].Value, inviterName, documentTitle);
        var html = BuildInviteHtml(inviterName, documentTitle, role, documentUrl);
        var text = string.Format(_localizer["InvitePlainText"].Value, inviterName, documentTitle, role, documentUrl);

        await SendEmailAsync(toEmail, subject, html, text);
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        if (string.IsNullOrEmpty(_settings.ResendApiKey))
        {
            _logger.LogWarning("Resend API key not configured — skipping email to {To}", to);
            return;
        }

        try
        {
            var payload = new
            {
                from = $"{_settings.FromName} <{_settings.FromAddress}>",
                to = new[] { to },
                subject,
                html = htmlBody,
                text = textBody
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("/emails", content);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Resend API error {Status}: {Body}", response.StatusCode, body);
                throw new InvalidOperationException($"Failed to send email: {response.StatusCode}");
            }

            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
            throw;
        }
    }

    private string BuildInviteHtml(string inviterName, string documentTitle, string role, string documentUrl)
    {
        var inviteBody = string.Format(_localizer["InviteBody"].Value, System.Net.WebUtility.HtmlEncode(inviterName));
        var roleLabel = _localizer["InviteRole"].Value;
        var buttonText = _localizer["InviteButton"].Value;
        var footerText = _localizer["InviteFooter"].Value;

        return $"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
        </head>
        <body style="margin:0;padding:0;background-color:#f8f9fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f8f9fa;padding:40px 20px;">
            <tr>
              <td align="center">
                <table width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                  <tr>
                    <td style="padding:32px 32px 0;">
                      <h1 style="margin:0 0 8px;font-size:20px;font-weight:600;color:#1a1a1a;">Lilia</h1>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:24px 32px;">
                      <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#333;">
                        {inviteBody}
                      </p>
                      <div style="background-color:#f0f4ff;border-radius:8px;padding:16px 20px;margin:0 0 20px;">
                        <p style="margin:0 0 4px;font-size:16px;font-weight:600;color:#1a1a1a;">
                          {System.Net.WebUtility.HtmlEncode(documentTitle)}
                        </p>
                        <p style="margin:0;font-size:13px;color:#666;">
                          {roleLabel} <strong>{System.Net.WebUtility.HtmlEncode(role)}</strong>
                        </p>
                      </div>
                      <a href="{System.Net.WebUtility.HtmlEncode(documentUrl)}"
                         style="display:inline-block;background-color:#1976d2;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;">
                        {buttonText}
                      </a>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:20px 32px;border-top:1px solid #eee;">
                      <p style="margin:0;font-size:12px;color:#999;">
                        {footerText}
                      </p>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
    }
}
