using System.Text;
using Microsoft.Extensions.Localization;

using Lilia.Api.Services.Email;

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
    public string ResendApiBase { get; set; } = "https://api.resend.com";

    /// <summary>
    /// Providers for account emails (sign-in, verify, reset, welcome, export, closure), in order:
    /// the first that accepts the email sends it, e.g. <c>resend,smtp</c>. Unset: <see cref="Transport"/>.
    /// See <see cref="Lilia.Api.Services.Email.EmailGateway"/>.
    /// </summary>
    public string? AccountRoute { get; set; }

    /// <summary>Providers for notifications (sharing, teams), in order. Unset: <see cref="Transport"/>.</summary>
    public string? NotificationRoute { get; set; }
    public string FromAddress { get; set; } = "noreply@liliaeditor.com";
    public string FromName { get; set; } = "Lilia";
    public string BaseUrl { get; set; } = "https://editor.liliaeditor.com";

    /// <summary>The publisher line under every designed email (name EI, SIREN, address): empty until LEGAL-6.</summary>
    public string? PublisherLine { get; set; }

    /// <summary>
    /// A second address for the same app (the test copy: http://localhost:3003, for the
    /// VM's own browser). When set, every email lists its links on this address too,
    /// under the usual ones (the user, 11 Oct). Unset in production.
    /// </summary>
    public string? AlsoBaseUrl { get; set; }

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
    private readonly EmailGateway _gateway;
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;
    private readonly IStringLocalizer<EmailService> _localizer;

    public EmailService(EmailSettings settings, ILogger<EmailService> logger, IStringLocalizer<EmailService> localizer, EmailGateway gateway)
    {
        _settings = settings;
        _logger = logger;
        _localizer = localizer;
        _gateway = gateway;
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

    public Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null) =>
        SendEmailCoreAsync(to, subject, htmlBody, textBody, null, EmailKind.Notification);

    /// <summary>An account email (the person asked for it or needs it): routed without Unsubscribe.</summary>
    public Task SendAccountEmailAsync(string to, string subject, string htmlBody, string? textBody = null) =>
        SendEmailCoreAsync(to, subject, htmlBody, textBody, null, EmailKind.Account);

    /// <summary>Send, with an optional Reply-To (the owner, when they allow replies), through the gateway.</summary>
    private async Task SendEmailCoreAsync(string to, string subject, string htmlBody, string? textBody, string? replyTo, EmailKind kind,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (!string.IsNullOrWhiteSpace(_settings.AlsoBaseUrl))
        {
            htmlBody = WithAlsoLinksHtml(htmlBody, _settings.BaseUrl, _settings.AlsoBaseUrl!);
            if (textBody != null) textBody = WithAlsoLinksText(textBody, _settings.BaseUrl, _settings.AlsoBaseUrl!);
        }
        await _gateway.SendAsync(new OutgoingEmail(to, subject, htmlBody, textBody, replyTo, kind, headers));
    }

    private static List<string> LinksOn(string body, string baseUrl, string pattern)
    {
        var b = baseUrl.TrimEnd('/');
        return System.Text.RegularExpressions.Regex.Matches(body, pattern)
            .Select(m => m.Groups[1].Value)
            .Where(u => u.StartsWith(b, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    /// <summary>Lists each link of the email again on the second address, under the email.</summary>
    public static string WithAlsoLinksHtml(string html, string baseUrl, string alsoBaseUrl)
    {
        var b = baseUrl.TrimEnd('/');
        var also = alsoBaseUrl.TrimEnd('/');
        var links = LinksOn(html, b, "href=\"([^\"]+)\"");
        if (links.Count == 0) return html;
        var host = new Uri(also).Authority;
        var items = string.Join("<br>", links.Select(u =>
        {
            var alt = also + u[b.Length..];
            var enc = System.Net.WebUtility.HtmlEncode(alt);
            return $"<a href=\"{enc}\" style=\"color:#0868A0\">{enc}</a>";
        }));
        var block = $"<div style=\"margin-top:20px;padding-top:12px;border-top:1px solid #d3d9e1;font:12px/1.5 -apple-system,Segoe UI,sans-serif;color:#56616e\">Same links on {System.Net.WebUtility.HtmlEncode(host)}:<br>{items}</div>";
        var at = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return at >= 0 ? html.Insert(at, block) : html + block;
    }

    public static string WithAlsoLinksText(string text, string baseUrl, string alsoBaseUrl)
    {
        var b = baseUrl.TrimEnd('/');
        var also = alsoBaseUrl.TrimEnd('/');
        var links = LinksOn(text, b, "(https?://[^\\s)\\]>\"]+)");
        if (links.Count == 0) return text;
        var host = new Uri(also).Authority;
        return text + $"\n\nSame links on {host}:\n" + string.Join("\n", links.Select(u => also + u[b.Length..]));
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

    // ── Sharing notices (Olivia, 10 Oct): two or three lines, at most one button ──

    public Task SendInviteAcceptedAsync(string toEmail, string acceptedByName, string documentTitle, string sharingUrl) =>
        SendEmailAsync(toEmail,
            $"{acceptedByName} joined \"{documentTitle}\"",
            NoticeHtml(
                $"<strong>{Enc(acceptedByName)}</strong> accepted your invitation to <strong>{Enc(documentTitle)}</strong>.",
                "They can open it now. See who has access, or change it, in Share.",
                "Open sharing", sharingUrl,
                "You're receiving this because you shared a document on Lilia."),
            $"{acceptedByName} accepted your invitation to \"{documentTitle}\".\n\nOpen sharing: {sharingUrl}\n\n— Lilia");

    public Task SendAccessChangedAsync(string toEmail, string changedByName, string documentTitle, string oldRole, string newRole, string documentUrl) =>
        SendEmailAsync(toEmail,
            $"Your access to \"{documentTitle}\" changed",
            NoticeHtml(
                $"<strong>{Enc(changedByName)}</strong> changed your access to <strong>{Enc(documentTitle)}</strong>.",
                $"{Enc(oldRole)} → <strong>{Enc(newRole)}</strong>",
                "Open document", documentUrl,
                "You're receiving this because a document was shared with you on Lilia."),
            $"{changedByName} changed your access to \"{documentTitle}\": {oldRole} → {newRole}.\n\nOpen document: {documentUrl}\n\n— Lilia");

    public Task SendAccessRemovedAsync(string toEmail, string removedByName, string documentTitle, string? replyTo = null) =>
        SendEmailCoreAsync(toEmail,
            $"Your access to \"{documentTitle}\" was removed",
            NoticeHtml(
                $"<strong>{Enc(removedByName)}</strong> removed your access to <strong>{Enc(documentTitle)}</strong>.",
                replyTo is null
                    ? "It no longer appears in your documents."
                    : $"It no longer appears in your documents. You can reply to this email to reach {Enc(removedByName)}.",
                null, null,
                "You're receiving this because a document was shared with you on Lilia."),
            $"{removedByName} removed your access to \"{documentTitle}\".\n\n— Lilia",
            replyTo, EmailKind.Notification);

    private static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>The sharing notices' one layout: the share email's card, a line, a second line, maybe a button.</summary>
    private static string NoticeHtml(string lineHtml, string secondHtml, string? button, string? url, string footer) => $$"""
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
                  <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#333;">{{lineHtml}}</p>
                  <p style="margin:0 0 20px;font-size:14px;line-height:1.6;color:#555;">{{secondHtml}}</p>
                  {{(button is null || url is null ? "" : $"<a href=\"{Enc(url)}\" style=\"display:inline-block;background-color:#1976d2;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:500;\">{Enc(button)}</a>")}}
                </td></tr>
                <tr><td style="padding:20px 32px;border-top:1px solid #eee;">
                  <p style="margin:0;font-size:12px;color:#999;">{{footer}}</p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

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
        => SendTemplateAsync("verify", toEmail, locale, magicLinkUrl);

    public Task SendStytchPasswordResetAsync(string toEmail, string resetUrl, string? locale)
        => SendTemplateAsync("reset", toEmail, locale, resetUrl);

    public Task SendStytchMagicLinkLoginAsync(string toEmail, string magicLinkUrl, string? locale)
        => SendTemplateAsync("signin", toEmail, locale, magicLinkUrl);

    /// <summary>Welcome to Lilia: once per account, after its address is known to be real (OnboardingService).</summary>
    public Task SendWelcomeAsync(string toEmail, string? firstName, string? locale)
        => SendTemplateAsync("welcome", toEmail, locale, _settings.BaseUrl.TrimEnd('/') + "/", firstName);

    /// <summary>
    /// The day-3 tip (Olivia, 21 Oct): three ways to start, sent once to someone who hasn't. Its
    /// "Don't send me tips" link is also the List-Unsubscribe header, one click (RFC 8058).
    /// </summary>
    public Task SendGettingStartedAsync(string toEmail, string? firstName, string? locale, string tipsUrl)
    {
        var email = EmailTemplates.Render("getting_started", locale, new Dictionary<string, string?>
        {
            ["action_url"] = _settings.BaseUrl.TrimEnd('/') + "/",
            ["recipient_email"] = toEmail,
            ["first_name"] = FirstName(firstName),
            ["tips_url"] = tipsUrl,
            ["publisher_line"] = _settings.PublisherLine,
        }, _settings.BaseUrl);
        return SendEmailCoreAsync(toEmail, email.Subject, email.Html, email.Text, null, EmailKind.Notification,
            new Dictionary<string, string>
            {
                ["List-Unsubscribe"] = $"<{tipsUrl}>",
                ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
            });
    }

    /// <summary>An account email from the 17 Oct designs (<see cref="EmailTemplates"/>), in the person's language.</summary>
    private Task SendTemplateAsync(string key, string toEmail, string? locale, string actionUrl, string? firstName = null)
    {
        var email = EmailTemplates.Render(key, locale, new Dictionary<string, string?>
        {
            ["action_url"] = actionUrl,
            ["recipient_email"] = toEmail,
            ["first_name"] = FirstName(firstName),
            ["publisher_line"] = _settings.PublisherLine,
        }, _settings.BaseUrl);
        return SendAccountEmailAsync(toEmail, email.Subject, email.Html, email.Text);
    }

    /// <summary>"Nadia Benali" → "Nadia": the designs greet by first name.</summary>
    public static string? FirstName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
}
