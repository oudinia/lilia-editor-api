namespace Lilia.Core.Entities;

/// <summary>
/// A single "What's new / fixed" entry, surfaced on the public /whats-new page.
/// Localized title/detail (en/fr/es) stored as jsonb so the client can render
/// in the reader's language. We log every shipped fix/feature here going
/// forward (status = "shipped"); "known" entries are issues testers may hit.
/// </summary>
public class ChangelogEntry
{
    public Guid Id { get; set; }

    /// <summary>Date the change shipped (drives grouping + ordering).</summary>
    public DateOnly EntryDate { get; set; }

    /// <summary>Area tag, e.g. "Block mode", "LaTeX", "Ask Lilia", "Theme".</summary>
    public string Area { get; set; } = "Editor";

    /// <summary>"fix" | "feature".</summary>
    public string Kind { get; set; } = "fix";

    /// <summary>"shipped" | "known".</summary>
    public string Status { get; set; } = "shipped";

    /// <summary>Localized title by language code (en required; fr/es optional).</summary>
    public Dictionary<string, string> Title { get; set; } = new();

    /// <summary>Localized detail by language code.</summary>
    public Dictionary<string, string> Detail { get; set; } = new();

    /// <summary>True once the fix was verified on the live editor.</summary>
    public bool Verified { get; set; }

    /// <summary>Optional screenshot URL (served from /whatsnew/...).</summary>
    public string? ShotUrl { get; set; }

    /// <summary>Tie-breaker ordering within a date (higher = earlier in the list).</summary>
    public int Sort { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
