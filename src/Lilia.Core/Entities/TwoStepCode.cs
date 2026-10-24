namespace Lilia.Core.Entities;

/// <summary>
/// A two-step code sent by email (two_step_codes, 25 Oct 2026): the owner, 24 Oct, "the login cannot be
/// just through the authenticator app, the choice of receiving code by email is important". Six digits,
/// 10 minutes, 5 tries; only its hash is kept. A newer code replaces an older one.
/// </summary>
public class TwoStepCode
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
