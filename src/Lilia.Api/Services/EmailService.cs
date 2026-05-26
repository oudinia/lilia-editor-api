using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Localization;
using MimeKit;

namespace Lilia.Api.Services;

public class EmailSettings
{
    /// <summary>
    /// Which transport to use when sending mail.
    ///   <c>resend</c> — production Resend HTTP API (default)
    ///   <c>smtp</c>   — local SMTP, intended for Mailpit in dev /
    ///                   automated e2e tests (no auth, no TLS by default)
    ///   <c>noop</c>   — log-only; emails are dropped. For unit tests.
    /// Case-insensitive.
    /// </summary>
    public string Transport { get; set; } = "resend";

    public string ResendApiKey { get; set; } = "";
    public string FromAddress { get; set; } = "noreply@liliaeditor.com";
    public string FromName { get; set; } = "Lilia";
    public string BaseUrl { get; set; } = "https://editor.liliaeditor.com";

    // ── SMTP transport (Mailpit / dev) ────────────────────────────────
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 1025;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    /// <summary>
    /// `none` (Mailpit default), `starttls`, or `ssl`. Anything else falls
    /// back to `none` — Mailpit accepts unencrypted SMTP by design.
    /// </summary>
    public string SmtpSecurity { get; set; } = "none";
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
        var transport = (_settings.Transport ?? "resend").Trim().ToLowerInvariant();
        switch (transport)
        {
            case "smtp":
                await SendViaSmtpAsync(to, subject, htmlBody, textBody);
                return;
            case "noop":
                _logger.LogInformation(
                    "[noop transport] would send email to {To}: {Subject} ({HtmlLen} chars HTML)",
                    to, subject, htmlBody.Length);
                return;
            case "resend":
            default:
                await SendViaResendAsync(to, subject, htmlBody, textBody);
                return;
        }
    }

    private async Task SendViaResendAsync(string to, string subject, string htmlBody, string? textBody)
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

            _logger.LogInformation("Email sent to {To}: {Subject} (via resend)", to, subject);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
            throw;
        }
    }

    private async Task SendViaSmtpAsync(string to, string subject, string htmlBody, string? textBody)
    {
        // MailKit's SmtpClient is the standard MimeKit-backed SMTP path.
        // For Mailpit (the intended dev target), there's no auth and no
        // TLS — security defaults to None. STARTTLS / SSL are wired so
        // the same transport works against real SMTP relays if a team
        // wants that flow.
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        if (!string.IsNullOrEmpty(textBody)) bodyBuilder.TextBody = textBody;
        message.Body = bodyBuilder.ToMessageBody();

        var security = (_settings.SmtpSecurity ?? "none").Trim().ToLowerInvariant() switch
        {
            "starttls" => SecureSocketOptions.StartTls,
            "ssl" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.None,
        };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, security);
            if (!string.IsNullOrEmpty(_settings.SmtpUsername))
            {
                await client.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword ?? "");
            }
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation(
                "Email sent to {To}: {Subject} (via smtp {Host}:{Port})",
                to, subject, _settings.SmtpHost, _settings.SmtpPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SMTP send failed for {To}: {Subject} ({Host}:{Port})",
                to, subject, _settings.SmtpHost, _settings.SmtpPort);
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

    public async Task SendTeamMemberRemovedAsync(string toEmail, string? recipientName,
        string removerName, string teamName)
    {
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hi" : $"Hi {recipientName}";
        var subject = $"You were removed from {teamName} on Lilia";
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
                    <strong>{{System.Net.WebUtility.HtmlEncode(removerName)}}</strong> removed you from the team:
                  </p>
                  <p style="margin:0 0 20px;font-size:17px;font-weight:600;line-height:1.4;color:#1a1a1a;font-family:ui-monospace,SFMono-Regular,monospace;">
                    {{System.Net.WebUtility.HtmlEncode(teamName)}}
                  </p>
                  <p style="margin:0;font-size:14px;color:#666;">
                    Documents you owned personally are unaffected. If this was unexpected, reach out to the team owner.
                  </p>
                </td></tr>
                <tr><td style="padding:20px 32px;border-top:1px solid #eee;">
                  <p style="margin:0;font-size:12px;color:#999;">
                    You're receiving this because your team membership changed on Lilia.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
        var text = $"{greeting},\n\n{removerName} removed you from the team:\n\n{teamName}\n\nDocuments you owned personally are unaffected.";
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

    // ─────────────────────────────────────────────────────────────────
    //  Stytch BYOM emails — fired from StytchWebhookController when
    //  Stytch posts the email-payload webhook. Same look + sender as
    //  every other transactional email; locale-aware via a small inline
    //  copy table (resx wiring can come later if it grows).
    // ─────────────────────────────────────────────────────────────────

    public Task SendStytchVerificationAsync(string toEmail, string magicLinkUrl, string? locale)
        => SendStytchAuthEmailAsync(StytchEmailKind.Verification, toEmail, magicLinkUrl, locale);

    public Task SendStytchPasswordResetAsync(string toEmail, string resetUrl, string? locale)
        => SendStytchAuthEmailAsync(StytchEmailKind.PasswordReset, toEmail, resetUrl, locale);

    public Task SendStytchMagicLinkLoginAsync(string toEmail, string magicLinkUrl, string? locale)
        => SendStytchAuthEmailAsync(StytchEmailKind.MagicLinkLogin, toEmail, magicLinkUrl, locale);

    private async Task SendStytchAuthEmailAsync(StytchEmailKind kind, string toEmail, string actionUrl, string? locale)
    {
        var copy = StytchEmailCopy.For(kind, locale);
        var html = BuildStytchAuthHtml(copy, actionUrl);
        var text = $"{copy.Heading}\n\n{copy.Body}\n\n{copy.Cta}: {actionUrl}\n\n{copy.Footer}";
        await SendEmailAsync(toEmail, copy.Subject, html, text);
    }

    private static string BuildStytchAuthHtml(StytchEmailCopy.Resolved copy, string actionUrl)
    {
        var safeUrl = System.Net.WebUtility.HtmlEncode(actionUrl);
        return $"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>{System.Net.WebUtility.HtmlEncode(copy.Subject)}</title>
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
                      <h2 style="margin:0 0 12px;font-size:18px;font-weight:600;color:#1a1a1a;">
                        {System.Net.WebUtility.HtmlEncode(copy.Heading)}
                      </h2>
                      <p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#444;">
                        {System.Net.WebUtility.HtmlEncode(copy.Body)}
                      </p>
                      <a href="{safeUrl}"
                         style="display:inline-block;background-color:#4F46E5;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;">
                        {System.Net.WebUtility.HtmlEncode(copy.Cta)}
                      </a>
                      <p style="margin:24px 0 0;font-size:13px;line-height:1.55;color:#888;">
                        {System.Net.WebUtility.HtmlEncode(copy.LinkFallback)}<br>
                        <span style="word-break:break-all;color:#666;">{safeUrl}</span>
                      </p>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:20px 32px;border-top:1px solid #eee;">
                      <p style="margin:0;font-size:12px;color:#999;">
                        {System.Net.WebUtility.HtmlEncode(copy.Footer)}
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

// =====================================================================
//  Stytch email copy table — kept inline because there are only three
//  templates × three locales. If this grows past ~10 strings per locale
//  it should move to .resx alongside the rest of EmailService's
//  localized strings.
// =====================================================================

internal enum StytchEmailKind { Verification, PasswordReset, MagicLinkLogin }

internal static class StytchEmailCopy
{
    public sealed record Resolved(string Subject, string Heading, string Body, string Cta, string LinkFallback, string Footer);

    public static Resolved For(StytchEmailKind kind, string? locale)
    {
        var lang = NormalizeLocale(locale);
        return kind switch
        {
            StytchEmailKind.Verification => lang switch
            {
                "fr" => new Resolved(
                    "Vérifiez votre adresse e-mail Lilia",
                    "Bienvenue sur Lilia",
                    "Cliquez sur le bouton ci-dessous pour vérifier votre adresse e-mail et finaliser la création de votre compte. Ce lien expire dans 30 minutes.",
                    "Vérifier mon e-mail",
                    "Le bouton ne fonctionne pas ? Copiez ce lien dans votre navigateur :",
                    "Si vous n'avez pas créé de compte Lilia, vous pouvez ignorer cet e-mail."),
                "es" => new Resolved(
                    "Verifica tu correo de Lilia",
                    "Bienvenido a Lilia",
                    "Haz clic en el botón de abajo para verificar tu correo y terminar de crear tu cuenta. Este enlace expira en 30 minutos.",
                    "Verificar mi correo",
                    "¿El botón no funciona? Copia este enlace en tu navegador:",
                    "Si no creaste una cuenta de Lilia, puedes ignorar este correo."),
                _ => new Resolved(
                    "Verify your Lilia email",
                    "Welcome to Lilia",
                    "Click the button below to verify your email and finish setting up your account. This link expires in 30 minutes.",
                    "Verify my email",
                    "Button not working? Copy this link into your browser:",
                    "If you didn't create a Lilia account, you can safely ignore this email."),
            },
            StytchEmailKind.PasswordReset => lang switch
            {
                "fr" => new Resolved(
                    "Réinitialisez votre mot de passe Lilia",
                    "Demande de réinitialisation",
                    "Vous avez demandé à réinitialiser votre mot de passe. Cliquez sur le bouton ci-dessous pour en choisir un nouveau. Ce lien expire dans 30 minutes.",
                    "Réinitialiser le mot de passe",
                    "Le bouton ne fonctionne pas ? Copiez ce lien dans votre navigateur :",
                    "Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail — votre mot de passe ne change pas."),
                "es" => new Resolved(
                    "Restablece tu contraseña de Lilia",
                    "Solicitud de restablecimiento",
                    "Solicitaste restablecer tu contraseña. Haz clic en el botón de abajo para elegir una nueva. Este enlace expira en 30 minutos.",
                    "Restablecer contraseña",
                    "¿El botón no funciona? Copia este enlace en tu navegador:",
                    "Si no solicitaste este restablecimiento, ignora este correo — tu contraseña no cambiará."),
                _ => new Resolved(
                    "Reset your Lilia password",
                    "Reset request received",
                    "You asked to reset your password. Click the button below to choose a new one. This link expires in 30 minutes.",
                    "Reset password",
                    "Button not working? Copy this link into your browser:",
                    "If you didn't request this reset, ignore this email — your password won't change."),
            },
            StytchEmailKind.MagicLinkLogin => lang switch
            {
                "fr" => new Resolved(
                    "Votre lien de connexion Lilia",
                    "Connectez-vous à Lilia",
                    "Cliquez sur le bouton ci-dessous pour vous connecter. Ce lien expire dans 30 minutes et ne peut être utilisé qu'une seule fois.",
                    "Se connecter à Lilia",
                    "Le bouton ne fonctionne pas ? Copiez ce lien dans votre navigateur :",
                    "Si vous n'êtes pas à l'origine de cette demande de connexion, ignorez cet e-mail."),
                "es" => new Resolved(
                    "Tu enlace de inicio de sesión de Lilia",
                    "Inicia sesión en Lilia",
                    "Haz clic en el botón de abajo para iniciar sesión. Este enlace expira en 30 minutos y solo puede usarse una vez.",
                    "Iniciar sesión en Lilia",
                    "¿El botón no funciona? Copia este enlace en tu navegador:",
                    "Si no solicitaste este inicio de sesión, ignora este correo."),
                _ => new Resolved(
                    "Your Lilia sign-in link",
                    "Sign in to Lilia",
                    "Click the button below to sign in. This link expires in 30 minutes and can only be used once.",
                    "Sign in to Lilia",
                    "Button not working? Copy this link into your browser:",
                    "If you didn't request this sign-in, ignore this email."),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return "en";
        var two = locale.Trim().ToLowerInvariant().Split('-', '_')[0];
        return two is "fr" or "es" ? two : "en";
    }
}
