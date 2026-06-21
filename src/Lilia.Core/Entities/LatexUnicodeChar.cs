namespace Lilia.Core.Entities;

/// <summary>
/// One row of the Unicode→LaTeX coverage catalog (<c>latex_unicode_map</c>).
///
/// Researchers routinely paste literal Unicode into prose — Greek letters
/// (γ, Δ, μ), math operators (×, ≤, →), typography (—, …, °). Under pdflatex
/// these abort the compile ("Unicode character not set up for use with
/// LaTeX"). Each row maps a codepoint to a LaTeX replacement that the render
/// service injects as a <c>\newunicodechar</c> shim, so the character compiles
/// instead of failing.
///
/// DB-authoritative, mirroring the <see cref="LatexToken"/> catalog: rows are
/// loaded into an in-memory map at startup, and characters that appear in
/// content but are NOT in the catalog surface as <c>unmapped_unicode_char</c>
/// telemetry so coverage gaps are visible and the set can grow over time.
/// </summary>
public class LatexUnicodeChar
{
    public Guid Id { get; set; }

    /// <summary>Unicode scalar value, e.g. 0x03B3 (γ). Unique.</summary>
    public int Codepoint { get; set; }

    /// <summary>The literal character, stored for readability/audit.</summary>
    public string Character { get; set; } = string.Empty;

    /// <summary>
    /// LaTeX that renders the character. Math symbols are wrapped in
    /// <c>\ensuremath{…}</c> so they are valid in both text and math mode.
    /// </summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>math | text | either — informational (the replacement already
    /// encodes mode-safety via \ensuremath where needed).</summary>
    public string Mode { get; set; } = "math";

    /// <summary>greek | math | typography | currency | other — for the
    /// coverage report and grouping.</summary>
    public string Category { get; set; } = "other";

    /// <summary>Package the replacement needs, if any (null = bundled in the
    /// standard preamble). Reserved for future per-char package precision.</summary>
    public string? PackageSlug { get; set; }

    /// <summary>full | shimmed | none.</summary>
    public string CoverageLevel { get; set; } = "full";

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
