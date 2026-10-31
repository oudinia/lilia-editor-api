using System.Text;

using Lilia.Api.Services.Email;
using Lilia.Core.Entities;

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

    public EmailService(EmailSettings settings, ILogger<EmailService> logger, EmailGateway gateway)
    {
        _settings = settings;
        _logger = logger;
        _gateway = gateway;
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

    // ── Sharing and teams (17 Oct designs): in the reader's language, the caller picks it ──

    /// <summary>To someone without an account: the invitation opens sign-up, then the document.</summary>
    public Task SendDocumentInviteAsync(string toEmail, EmailActor inviter, string documentTitle, string role, string documentUrl, string? locale) =>
        SendNoticeAsync("invitation", toEmail, locale, inviter, null, new()
        {
            ["doc_title"] = documentTitle,
            ["role_line"] = RoleLine(role, locale),
            ["action_url"] = documentUrl,
        });

    public Task SendDocumentSharedAsync(string toEmail, EmailActor inviter, string documentTitle, string role, string documentUrl, string? locale) =>
        SendNoticeAsync("shared", toEmail, locale, inviter, null, new()
        {
            ["doc_title"] = documentTitle,
            ["role_line"] = RoleLine(role, locale),
            ["action_url"] = documentUrl,
        });

    public Task SendInviteAcceptedAsync(string toEmail, EmailActor joined, string documentTitle, string? role, string sharingUrl, string? locale) =>
        SendNoticeAsync("accepted", toEmail, locale, joined, null, new()
        {
            ["doc_title"] = documentTitle,
            ["role_line"] = role is null ? null : RoleLine(role, locale),
            ["action_url"] = sharingUrl,
        });

    public Task SendAccessChangedAsync(string toEmail, EmailActor changedBy, string documentTitle, string oldRole, string newRole, string documentUrl, string? locale)
    {
        var (now, before) = EmailTemplates.Language(locale) switch
        {
            "fr" => ("Maintenant\u00A0: ", "Avant\u00A0: "),
            "es" => ("Ahora: ", "Antes: "),
            _ => ("Now: ", "Before: "),
        };
        return SendNoticeAsync("changed", toEmail, locale, changedBy, null, new()
        {
            ["doc_title"] = documentTitle,
            ["role_line"] = now + RoleLine(newRole, locale),
            ["previous_role"] = before + RoleLine(oldRole, locale),
            ["action_url"] = documentUrl,
        });
    }

    /// <summary>No button. When the owner allows replies, Reply-To is them and the email says so.</summary>
    public Task SendAccessRemovedAsync(string toEmail, EmailActor removedBy, string documentTitle, bool allowReply, string? locale) =>
        SendNoticeAsync("removed", toEmail, locale, removedBy, allowReply ? removedBy.Email : null, new()
        {
            ["doc_title"] = documentTitle,
            ["date"] = EmailTemplates.Date(DateTime.UtcNow, locale),
            ["reply_to_owner"] = allowReply && !string.IsNullOrWhiteSpace(removedBy.Email) ? "1" : null,
        });

    public Task SendTeamInviteAsync(string toEmail, EmailActor inviter, string teamName, string role, string acceptUrl, string? locale) =>
        SendNoticeAsync("team_invite", toEmail, locale, inviter, null, new()
        {
            ["team_name"] = teamName,
            ["team_initial"] = Initials(teamName, 1),
            ["role_line"] = RoleLine(role, locale),
            ["action_url"] = acceptUrl,
        });

    public Task SendTeamWelcomeAsync(string toEmail, string teamName, string teamCodename, string teamUrl, string? locale) =>
        SendNoticeAsync("team_ready", toEmail, locale, null, null, new()
        {
            ["team_name"] = teamName,
            ["team_initial"] = Initials(teamName, 1),
            ["team_codename"] = teamCodename,
            ["action_url"] = teamUrl,
        });

    public Task SendTeamMemberRemovedAsync(string toEmail, EmailActor removedBy, string teamName, string? locale) =>
        SendNoticeAsync("team_removed", toEmail, locale, removedBy, null, new()
        {
            ["team_name"] = teamName,
            ["team_initial"] = Initials(teamName, 1),
            ["date"] = EmailTemplates.Date(DateTime.UtcNow, locale),
        });

    private Task SendNoticeAsync(string key, string toEmail, string? locale, EmailActor? actor, string? replyTo, Dictionary<string, string?> values)
    {
        values["recipient_email"] = toEmail;
        values["publisher_line"] = _settings.PublisherLine;
        if (actor is not null)
        {
            values["actor_name"] = actor.Name;
            values["actor_email"] = actor.Email;
            values["actor_initials"] = Initials(actor.Name, 2);
        }
        var email = EmailTemplates.Render(key, locale, values, _settings.BaseUrl);
        return SendEmailCoreAsync(toEmail, email.Subject, email.Html, email.Text, replyTo, EmailKind.Notification);
    }

    /// <summary>The share drawer's words for a role, in the email's language: Can edit, Peut modifier, Puede editar.</summary>
    public static string RoleLine(string role, string? locale)
    {
        var lang = EmailTemplates.Language(locale);
        var normalized = RoleNames.Normalize(role);
        return (normalized, lang) switch
        {
            ("owner", "fr") => "Propriétaire", ("owner", "es") => "Propietario", ("owner", _) => "Owner",
            ("editor", "fr") => "Peut modifier", ("editor", "es") => "Puede editar", ("editor", _) => "Can edit",
            ("commenter" or "comment", "fr") => "Peut commenter", ("commenter" or "comment", "es") => "Puede comentar", ("commenter" or "comment", _) => "Can comment",
            ("viewer", "fr") => "Peut lire", ("viewer", "es") => "Puede ver", ("viewer", _) => "Can view",
            _ => role,
        };
    }

    /// <summary>"Nadia Benali" → "NB", "ana@example.org" → "A": the disc beside a name.</summary>
    public static string Initials(string? name, int max)
    {
        var source = (name ?? "").Trim();
        if (source.Contains('@')) { source = source[..source.IndexOf('@')]; max = 1; }
        var words = source.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Take(max).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length > 0 ? initials : "?";
    }

    // ── Account notices (17 Oct designs): the export, closing, erasing ──

    public Task SendExportReadyAsync(string toEmail, string? locale, DateTime requestedAt, DateTime expiresAt, string fileName, int documents, long bytes)
    {
        var lang = EmailTemplates.Language(locale);
        var megabytes = Math.Max(1, (int)Math.Round(bytes / 1_048_576.0));
        var meta = lang switch
        {
            "fr" => $"{documents} document{(documents == 1 ? "" : "s")} · {megabytes} Mo",
            "es" => $"{documents} documento{(documents == 1 ? "" : "s")} · {megabytes} MB",
            _ => $"{documents} document{(documents == 1 ? "" : "s")} · {megabytes} MB",
        };
        return SendAccountTemplateAsync("export", toEmail, locale, new()
        {
            ["action_url"] = _settings.BaseUrl.TrimEnd('/') + "/settings/data",
            ["date"] = EmailTemplates.Date(requestedAt, locale),
            ["expires_on"] = EmailTemplates.Date(expiresAt, locale),
            ["export_name"] = fileName,
            ["export_meta"] = meta,
        });
    }

    public Task SendAccountClosedAsync(string toEmail, string? locale, DateTime closedAt, DateTime eraseOn) =>
        SendAccountTemplateAsync("closed", toEmail, locale, new()
        {
            ["date"] = EmailTemplates.Date(closedAt, locale),
            ["erase_date"] = EmailTemplates.Date(eraseOn, locale),
        });

    /// <summary>Thirty days later: the "closed" layout, nothing left to restore.</summary>
    public Task SendAccountErasedAsync(string toEmail, string? locale, DateTime erasedAt) =>
        SendAccountTemplateAsync("erased", toEmail, locale, new()
        {
            ["date"] = EmailTemplates.Date(erasedAt, locale),
        });

    private Task SendAccountTemplateAsync(string key, string toEmail, string? locale, Dictionary<string, string?> values)
    {
        values["recipient_email"] = toEmail;
        values["publisher_line"] = _settings.PublisherLine;
        var email = EmailTemplates.Render(key, locale, values, _settings.BaseUrl);
        return SendAccountEmailAsync(toEmail, email.Subject, email.Html, email.Text);
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

    /// <summary>A two-step code (25 Oct): the sign-in email's design, the code where the button was.</summary>
    public Task SendTwoStepCodeAsync(string toEmail, string code, string? locale)
    {
        var email = EmailTemplates.Render("two_step_code", locale, new Dictionary<string, string?>
        {
            ["code"] = code,
            ["recipient_email"] = toEmail,
            ["publisher_line"] = _settings.PublisherLine,
        }, _settings.BaseUrl);
        return SendAccountEmailAsync(toEmail, email.Subject, email.Html, email.Text);
    }

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
