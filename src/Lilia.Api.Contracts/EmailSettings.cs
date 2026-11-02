namespace Lilia.Api.Services;

/// <summary>Email transport and routes (moved from EmailService.cs, 2 Nov 2026: the Sharing slice, a project of
/// its own, sends its emails through IEmailService and reads these settings).</summary>
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
    /// See Lilia.Api.Services.Email.EmailGateway.
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
