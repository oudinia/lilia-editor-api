namespace Lilia.Core.Entities;

/// <summary>
/// Per-session tally of how often a given LaTeX token appeared in a user's
/// import. Populated by the parser via bulk upsert when a review session
/// is staged. Powers the "Coverage" tab on review + fleet-wide dashboards
/// ("top 10 unsupported tokens seen in last 30 days").
/// </summary>
public class LatexTokenUsage
{
    public Guid Id { get; set; }

    public Guid TokenId { get; set; }

    /// <summary>
    /// Link to the import review session so we can filter per-document.
    /// Cascade-deletes with the session (same as diagnostics).
    /// </summary>
    public Guid SessionId { get; set; }

    public int Count { get; set; } = 1;

    /// <summary>When the parser first saw this token in this session.</summary>
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the parser last saw this token in this session.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual LatexToken Token { get; set; } = null!;
    public virtual ImportReviewSession Session { get; set; } = null!;
}
