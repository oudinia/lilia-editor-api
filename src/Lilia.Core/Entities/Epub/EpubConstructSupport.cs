namespace Lilia.Core.Entities.Epub;

/// <summary>
/// Whether one construct exists in one specification version, and in what state. No row means the version
/// doesn't have it (MathML in EPUB 2.0.1, <c>image/avif</c> as a core type before 3.4).
///
/// EPUB phase 1 (20 Nov 2026): the states follow the specifications' own words. EPUB 3.3 calls the NCX and the
/// <c>guide</c> "legacy" (allowed for EPUB 2 readers, ignored by EPUB 3 readers); the 3.4 draft calls them
/// "outdated" (EPUBCheck reports a usage note, not an error). "Deprecated" is a warning: a new file shouldn't
/// write it (EPUBCheck <c>OPF-086b</c> for <c>epub:type="annotation"</c>, <c>RSC-017</c> for
/// <c>doc-biblioentry</c>).
/// </summary>
public class EpubConstructSupport
{
    public Guid ConstructId { get; set; }
    public string VersionSlug { get; set; } = string.Empty;

    /// <summary>CHECK: supported / legacy / outdated / deprecated / removed.</summary>
    public string Status { get; set; } = "supported";

    public string? Notes { get; set; }

    public virtual EpubConstruct? Construct { get; set; }
    public virtual EpubSpecVersion? Version { get; set; }
}
