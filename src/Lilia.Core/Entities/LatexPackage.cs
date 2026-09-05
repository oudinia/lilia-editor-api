namespace Lilia.Core.Entities;

/// <summary>
/// A LaTeX package entry — the catalog's coarse unit. Ships with a
/// coverage level that tells the parser + UI how we handle this package
/// when it appears in an import. Missing-from-catalog = "we've never
/// seen it"; unsupported = "we've seen it and it's known to break".
/// Seeded with the top ~50 packages; the parser auto-inserts new ones
/// with coverage_level = 'unsupported' on first sighting.
/// </summary>
public class LatexPackage
{
    /// <summary>CTAN short name — used as FK target. e.g. "amsmath", "tikz".</summary>
    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// High-level grouping for the UI. CHECK-constrained at DB layer.
    /// Values: math / graphics / bibliography / layout / language /
    /// font / cv / presentation / code / table / reference / utility.
    /// </summary>
    public string Category { get; set; } = "utility";

    /// <summary>
    /// full — everything maps cleanly to Lilia blocks.
    /// partial — core commands work, edge cases fall through.
    /// shimmed — we rewrite it at import via a class-aware shim.
    /// none — we detect it but don't render it.
    /// unsupported — known to break the parser; auto-rejected.
    /// </summary>
    public string CoverageLevel { get; set; } = "none";

    /// <summary>
    /// The minimum engine this package needs — <c>lualatex</c> / <c>xelatex</c>,
    /// or null when it runs anywhere. Mirrors
    /// <see cref="LatexDocumentClass.DefaultEngine"/>.
    ///
    /// <para>Engine detection was a hand-maintained regex holding eleven command
    /// names, so a package it had never heard of was compiled by an engine that
    /// could not read it and the author was told their document was broken.
    /// Recording the requirement here makes adding a package a row rather than a
    /// code change, and lets the compiler be aimed correctly the first time
    /// instead of after a wasted run.</para>
    /// </summary>
    public string? RequiresEngine { get; set; }

    public string? CoverageNotes { get; set; }

    public string? CtanUrl { get; set; }

    /// <summary>Free-form version string if we target a specific release.</summary>
    public string? Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual ICollection<LatexToken> Tokens { get; set; } = new List<LatexToken>();
}
