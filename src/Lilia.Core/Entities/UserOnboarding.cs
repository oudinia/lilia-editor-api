namespace Lilia.Core.Entities;

/// <summary>
/// Where a person is in their first steps with Lilia (21 Oct 2026, reports/2026-10-21-welcome-onboarding.md):
/// one row per account, timestamps only. Never anything they wrote: the admin console reads it as counts.
/// </summary>
public class UserOnboarding
{
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>The address is known to be real: email verified, or a Google sign-up.</summary>
    public DateTime? VerifiedAt { get; set; }
    public DateTime? WelcomeSentAt { get; set; }
    public DateTime? FirstDocumentAt { get; set; }
    /// <summary>blank, template, import or describe: how the first document started.</summary>
    public string? FirstDocumentRoute { get; set; }
    /// <summary>The furthest tour step reached (1–4), so we see where people skip.</summary>
    public int? TourStepReached { get; set; }
    /// <summary>The tour was finished or skipped; it shows again only from Help → Take the tour.</summary>
    public DateTime? TourSeenAt { get; set; }
    /// <summary>Times Help → Take the tour restarted it (TourSeenAt stays set).</summary>
    public int TourRestarts { get; set; }
    public DateTime? FirstValidatePassAt { get; set; }
    public DateTime? FirstExportAt { get; set; }
    public DateTime? FollowUpSentAt { get; set; }
    /// <summary>"Don't send me tips": the day-3 follow-up and any later tip are never sent.</summary>
    public bool TipsOptOut { get; set; }
    /// <summary>Random, set when a tip is sent: its one-click "Don't send me tips" link carries it.</summary>
    public string? TipsToken { get; set; }
    /// <summary>IANA time zone from the browser at sign-up (the follow-up is sent at 10:00 there).</summary>
    public string? TimeZone { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
}
