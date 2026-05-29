namespace Lilia.Core.Entities;

/// <summary>
/// Stores a single user-triggered diagnostic bundle from the math
/// editor (or any other client surface that wants to ship the
/// equivalent of a Redux-DevTools snapshot for analysis).
///
/// The bundle is opaque JSON — schema lives on the client side. The
/// server's only job is to persist it and hand back a short
/// human-readable <see cref="RefToken"/> that the user can paste
/// when sharing a session (chat, ticket, etc.). Lookup by token is
/// scoped to either the original author or a privileged role; see
/// <c>DiagnosticsController</c> for the access policy.
///
/// Not subject to retention beyond a soft cap; expected usage is
/// "minutes-to-days" debugging windows. A future cron can prune rows
/// older than, say, 30 days without breaking any contract.
/// </summary>
public class DiagnosticCapture
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stytch user id. Null when the capture was triggered before
    /// the auth middleware resolved a user (rare — usually only the
    /// first-paint diagnostic). Captures with NULL UserId are only
    /// readable by admins.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Short shareable identifier — kebab-case, 10–14 chars. Generated
    /// server-side from a UUID so the user can paste e.g.
    /// <c>cap-7a3b2c9f</c> into chat instead of the full GUID.
    /// </summary>
    public string RefToken { get; set; } = string.Empty;

    /// <summary>
    /// What surface produced the capture: <c>math-editor</c>,
    /// <c>flow-editor</c>, etc. Free-form string; the analyst uses
    /// it to pick the right viewer.
    /// </summary>
    public string Source { get; set; } = "math-editor";

    /// <summary>
    /// Short title or repro description from the user (optional). The
    /// DevTools panel prompts for it as "What happened?". Capped at
    /// 200 chars; the body of the report lives in <see cref="Payload"/>.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// User agent string at capture time.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Page URL at capture time (e.g. <c>/document/abc/equations</c>).
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// The full diagnostic JSON bundle. Stored as jsonb so a future
    /// admin viewer can grep into specific fields without parsing
    /// the whole document.
    /// </summary>
    public string Payload { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
