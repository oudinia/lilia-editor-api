namespace Lilia.Core.Entities;

// =====================================================================
//  Companion-app waitlist — bridge between launch (mobile read-mode
//  responsive shell) and the native companion app.
//
//  The banner inside StudioMobile (and shortly the dashboard) shows a
//  "Notify me when the companion app ships" CTA. Submissions land here.
//
//  Three uses:
//   1. Demand signal — how big is the mobile-editing audience?
//   2. Launch list — email everyone on the waitlist when the native
//      app ships.
//   3. Marketing social-proof — "join 1,200 waiting" once volume is
//      reasonable.
//
//  No PII beyond email + locale + UA. user_id is captured when the
//  user is signed in (most cases), nullable for anonymous catches.
//  See lilia-docs/launch-readiness/2026-05-18-mobile-companion-
//  postponed.md §6.
// =====================================================================

public class CompanionAppWaitlist
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }           // null = anonymous
    public string Email { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public string? UserAgent { get; set; }
    /// <summary>'banner' | 'dashboard' | 'marketing-page'</summary>
    public string Source { get; set; } = "banner";
    public DateTime SignedUpAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
}
