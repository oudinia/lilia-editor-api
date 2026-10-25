namespace Lilia.Core.Entities;

/// <summary>
/// A browser the author trusts for 30 days (trusted_devices, 25 Oct 2026): there, a password or Google
/// sign-in is enough, without the second step. The browser keeps a random token in an HttpOnly cookie;
/// only its SHA-256 is kept here. Forgotten one by one in Settings → Security, and all at once when
/// two-step is turned off, on "sign out everywhere", or by support.
/// </summary>
public class TrustedDevice
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    /// <summary>"Chrome on Windows", from the user agent when it was trusted.</summary>
    public string Label { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
