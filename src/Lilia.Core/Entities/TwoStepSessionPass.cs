namespace Lilia.Core.Entities;

/// <summary>
/// A Stytch session that passed the second step with an email code (two_step_session_passes, 25 Oct
/// 2026). Stytch records authenticator codes in the session itself; an email code is Lilia's, so Lilia
/// remembers the session. Kept until the session would have ended (7 days).
/// </summary>
public class TwoStepSessionPass
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
