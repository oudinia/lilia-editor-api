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

    public async Task SendTeamWelcomeAsync(string toEmail, string? firstName, string teamCodename)
    {
        // Sent on user.created right after the welcome — announces the
        // auto-generated default team so the user knows they already
        // have a workspace to play in. Code is a research-lab-style
        // codename like "Cobalt Photon A7B" from TeamCodenameGenerator.
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hi there" : $"Hi {firstName}";
        var subject = $"Your team is ready — {teamCodename}";
        var html = $$"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
        <body style="margin:0;padding:0;background-color:#f8f9fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f8f9fa;padding:40px 20px;">
            <tr><td align="center">
              <table width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                <tr><td style="padding:32px 32px 0;">
                  <h1 style="margin:0 0 8px;font-size:20px;font-weight:600;color:#1a1a1a;">Lilia</h1>
                </td></tr>
                <tr><td style="padding:24px 32px;">
                  <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#333;">{{greeting}},</p>
                  <p style="margin:0 0 8px;font-size:15px;line-height:1.6;color:#333;">
                    Your default team is ready. Codename:
                  </p>
                  <p style="margin:0 0 20px;font-size:18px;font-weight:600;line-height:1.4;color:#1976d2;font-family:ui-monospace,SFMono-Regular,monospace;">
                    {{teamCodename}}
                  </p>
                  <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#333;">
                    Every document you create lives in this team by default. You can rename it,
                    invite up to 2 collaborators on the free plan, or roll a fresh codename
                    from your team settings.
                  </p>
                  <a href="{{_settings.BaseUrl}}/latex/docs"
                     style="display:inline-block;background-color:#1976d2;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;">
                    Open Lilia
                  </a>
                </td></tr>
                <tr><td style="padding:20px 32px;border-top:1px solid #eee;">
                  <p style="margin:0;font-size:12px;color:#999;">— The Lilia team</p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
        var text = $"{greeting},\n\nYour default team is ready. Codename: {teamCodename}\n\nEvery document you create lives in this team by default. Open Lilia: {_settings.BaseUrl}/latex/docs\n\n— The Lilia team";
        await SendEmailAsync(toEmail, subject, html, text);
    }

    public async Task SendWelcomeAsync(string toEmail, string? firstName)
    {
        // Welcome email sent on Kinde user.created webhook (LILIA-95).
        // Kept short, single-CTA — first impression beats feature-tour walls.
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hi there" : $"Hi {firstName}";
        var subject = "Welcome to Lilia";
        var html = $$"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
        <body style="margin:0;padding:0;background-color:#f8f9fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f8f9fa;padding:40px 20px;">
            <tr><td align="center">
              <table width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                <tr><td style="padding:32px 32px 0;">
                  <h1 style="margin:0 0 8px;font-size:20px;font-weight:600;color:#1a1a1a;">Lilia</h1>
                </td></tr>
                <tr><td style="padding:24px 32px;">
                  <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#333;">{{greeting}} — welcome aboard.</p>
                  <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#333;">
                    Lilia is a writing studio for academic and technical work — block editor, LaTeX preview,
                    DOCX/PDF import, and one-shot conversion. Open your dashboard to start with a blank doc
                    or import an existing one.
                  </p>
                  <a href="{{_settings.BaseUrl}}/latex/docs"
                     style="display:inline-block;background-color:#1976d2;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;">
                    Open Lilia
                  </a>
                </td></tr>
                <tr><td style="padding:20px 32px;border-top:1px solid #eee;">
                  <p style="margin:0;font-size:12px;color:#999;">
                    Reply to this email if you hit anything — early users get direct support.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
        var text = $"{greeting} — welcome to Lilia.\n\nLilia is a writing studio for academic and technical work — block editor, LaTeX preview, DOCX/PDF import, and one-shot conversion.\n\nOpen your dashboard: {_settings.BaseUrl}/latex/docs\n\nReply to this email if you hit anything — early users get direct support.";
        await SendEmailAsync(toEmail, subject, html, text);
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        if (string.IsNullOrEmpty(_settings.ResendApiKey))
        {
            // Raised from Warning to Error — every "invitation sent" UI
            // confirmation without a delivered email is worse than a
            // visible failure. Lands in Sentry so the missing env var is
            // obvious rather than being dropped on the floor.
            _logger.LogError("Resend API key not configured — email to {To} ({Subject}) NOT sent. Set Email__ResendApiKey in the app environment.", to, subject);
            throw new InvalidOperationException("Email provider (Resend) not configured on this server.");
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

    public async Task SendDocumentSharedAsync(string toEmail, string? recipientName, string inviterName,
        string documentTitle, string permission, string documentUrl)
    {
        // By-id share path — the recipient already has an account (we found
        // them via user-search), so we link straight to the doc rather than
        // to sign-up. No localization yet; mirrors SendTeamWelcomeAsync.
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi" : $"Hi {recipientName}";
        var subject = $"{inviterName} shared \"{documentTitle}\" with you";
        var html = $$"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
        <body style="margin:0;padding:0;background-color:#f8f9fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f8f9fa;padding:40px 20px;">
            <tr><td align="center">
              <table width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                <tr><td style="padding:32px 32px 0;">
                  <h1 style="margin:0 0 8px;font-size:20px;font-weight:600;color:#1a1a1a;">Lilia</h1>
                </td></tr>
                <tr><td style="padding:24px 32px;">
                  <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#333;">{{greeting}},</p>
                  <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#333;">
                    <strong>{{System.Net.WebUtility.HtmlEncode(inviterName)}}</strong> shared a document with you on Lilia:
                  </p>
                  <p style="margin:0 0 20px;font-size:17px;font-weight:600;line-height:1.4;color:#1a1a1a;">
                    {{System.Net.WebUtility.HtmlEncode(documentTitle)}}
                  </p>
                  <p style="margin:0 0 20px;font-size:14px;color:#666;">
                    Your access level: <strong>{{System.Net.WebUtility.HtmlEncode(permission)}}</strong>
                  </p>
                  <a href="{{System.Net.WebUtility.HtmlEncode(documentUrl)}}"
                     style="display:inline-block;background-color:#1976d2;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;">
                    Open document
                  </a>
                </td></tr>
                <tr><td style="padding:20px 32px;border-top:1px solid #eee;">
                  <p style="margin:0;font-size:12px;color:#999;">
                    You're receiving this because someone shared a document with you on Lilia.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
        var text = $"{greeting},\n\n{inviterName} shared a document with you on Lilia:\n\n{documentTitle}\n\nYour access level: {permission}\n\nOpen document: {documentUrl}";
        await SendEmailAsync(toEmail, subject, html, text);
    }

    public async Task SendTeamInviteAsync(string toEmail, string inviterName, string teamName,
        string role, string acceptUrl)
    {
        var subject = $"{inviterName} invited you to {teamName} on Lilia";
        var html = $$"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
        <body style="margin:0;padding:0;background-color:#f8f9fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f8f9fa;padding:40px 20px;">
            <tr><td align="center">
              <table width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                <tr><td style="padding:32px 32px 0;">
                  <h1 style="margin:0 0 8px;font-size:20px;font-weight:600;color:#1a1a1a;">Lilia</h1>
                </td></tr>
                <tr><td style="padding:24px 32px;">
                  <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#333;">Hi,</p>
                  <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#333;">
                    <strong>{{System.Net.WebUtility.HtmlEncode(inviterName)}}</strong> invited you to join their team on Lilia:
                  </p>
                  <p style="margin:0 0 16px;font-size:18px;font-weight:600;line-height:1.4;color:#1976d2;font-family:ui-monospace,SFMono-Regular,monospace;">
                    {{System.Net.WebUtility.HtmlEncode(teamName)}}
                  </p>
                  <p style="margin:0 0 24px;font-size:14px;color:#666;">
                    Your role: <strong>{{System.Net.WebUtility.HtmlEncode(role)}}</strong>
                  </p>
                  <a href="{{System.Net.WebUtility.HtmlEncode(acceptUrl)}}"
                     style="display:inline-block;background-color:#1976d2;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;">
                    Accept invite
                  </a>
                </td></tr>
                <tr><td style="padding:20px 32px;border-top:1px solid #eee;">
                  <p style="margin:0;font-size:12px;color:#999;">
                    You're receiving this because someone invited you to a team on Lilia.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
        var text = $"Hi,\n\n{inviterName} invited you to join their team on Lilia:\n\n{teamName}\n\nYour role: {role}\n\nAccept invite: {acceptUrl}";
        await SendEmailAsync(toEmail, subject, html, text);
    }
}
