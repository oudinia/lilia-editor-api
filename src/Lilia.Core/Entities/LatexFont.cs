namespace Lilia.Core.Entities;

/// <summary>
/// One row per font entry in the TUG LaTeX Font Catalogue
/// (https://tug.org/FontCatalogue/) — ~523 fonts after scrape.
///
/// Drives the document-level font picker in the UI and the preamble
/// dispatch in <c>LaTeXPreambleBuilder</c>: pdflatex docs emit the
/// classic `\usepackage{...}` form; lua/xelatex docs emit the
/// `\setmainfont{...}` fontspec form. The picker can also surface
/// fontspec-only fonts (those with `OtfOnly = true`); selecting one
/// auto-bumps the document engine to lualatex via EngineDetector.
///
/// Catalogue is seeded from a one-time scrape committed at
/// <c>data/tug-fonts.json</c>; subsequent updates roll forward via
/// EF migration so dev/staging/prod stay in sync.
/// </summary>
public class LatexFont
{
    /// <summary>TUG slug, e.g. "accanthis", "qtagatetype", "tgadventor".</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Human-readable name, e.g. "Accanthis", "QT AgateType".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// CHECK: serif / sans-serif / monospace / calligraphic / uncial /
    /// blackletter / display / other. Drives the picker's category groups.
    /// First (primary) category if a font is listed under multiple TUG
    /// pages — full list lives in <see cref="AdditionalCategories"/>.
    /// </summary>
    public string Category { get; set; } = "other";

    /// <summary>
    /// JSON array of extra category slugs when a font appears in more than
    /// one TUG section (e.g. a display-quality serif). Picker can filter
    /// across multiple categories without reading another row.
    /// </summary>
    public string? AdditionalCategories { get; set; }

    /// <summary>Listed on tug.org/FontCatalogue/mathfonts.html.</summary>
    public bool HasMath { get; set; }

    /// <summary>True if the font ships an OTF/TTF file (fontspec-friendly).</summary>
    public bool HasOtf { get; set; }

    /// <summary>True for `[OTF or TTF only]` fonts — pdflatex path unavailable.</summary>
    public bool OtfOnly { get; set; }

    /// <summary>
    /// Pdflatex usage — JSON `{ "fontencOption": "T1"|null,
    /// "packages": [{ "name": "accanthis", "options": "..." }, ...],
    /// "renewCommands": ["sfdefault"|"ttdefault"|...] }`.
    /// Null if the font is fontspec-only.
    /// </summary>
    public string? PdflatexUsage { get; set; }

    /// <summary>
    /// Name to feed `\setmainfont{...}` under fontspec. Often equal to
    /// <see cref="DisplayName"/> but sometimes camel-cased differently
    /// (e.g. "QTAgateType" vs display "QT AgateType"). Null for the
    /// rare pdflatex-only fonts that have no OTF distribution.
    /// </summary>
    public string? FontspecName { get; set; }

    /// <summary>URL to TUG's preview PNG/SVG (hot-link, not bundled).</summary>
    public string? PreviewImageUrl { get; set; }

    /// <summary>URL to the TUG detail page so the picker can show "more info".</summary>
    public string? DetailUrl { get; set; }

    /// <summary>
    /// True only when the API container has the font's OTF/TTF available
    /// (i.e. installed via texlive-fonts-extra or apt). Picker greys out
    /// uninstalled fonts so users don't pick something the server can't
    /// compile. Maintained by hand as the Dockerfile bumps in.
    /// </summary>
    public bool InstalledInContainer { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
