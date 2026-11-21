namespace Lilia.Core.Entities.Epub;

/// <summary>
/// One EPUB specification the catalog knows: the package formats (EPUB 2.0.1, 3.0, 3.3, 3.4) and the
/// accessibility specifications (EPUB Accessibility 1.1, 1.2). Every construct says, per version, whether it
/// exists there and in what state (<see cref="EpubConstructSupport"/>).
///
/// EPUB phase 1 (20 Nov 2026, lilia-docs/reports/2026-11-20-epub-export-audit.md): the catalog is modelled
/// before the code, as the LaTeX catalog should have been, so import and export can both read it. A 3.x book
/// still writes <c>version="3.0"</c> in its package (<see cref="PackageVersion"/>): each 3.x is a continuation
/// of EPUB 3, which is why targeting 3.4 doesn't make a file unreadable by a 3.0 reader.
/// </summary>
public class EpubSpecVersion
{
    /// <summary>Stable key: <c>epub-2.0.1</c>, <c>epub-3.0</c>, <c>epub-3.3</c>, <c>epub-3.4</c>,
    /// <c>epub-a11y-1.1</c>, <c>epub-a11y-1.2</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>CHECK: <c>epub</c> (a package format) / <c>epub-a11y</c> (an accessibility specification).</summary>
    public string Family { get; set; } = "epub";

    /// <summary>CHECK: <c>recommendation</c> / <c>candidate-recommendation-draft</c> / <c>superseded</c>.</summary>
    public string Status { get; set; } = "recommendation";

    /// <summary>Date of the edition the seed was read from.</summary>
    public DateOnly PublishedOn { get; set; }

    /// <summary>The package's <c>version</c> attribute a file of this spec writes ("2.0" or "3.0"); null for
    /// the accessibility specifications, which add metadata to an EPUB 3 package.</summary>
    public string? PackageVersion { get; set; }

    /// <summary>The EPUBCheck release that checks against this spec (null when none does).</summary>
    public string? EpubCheckVersion { get; set; }

    /// <summary>True for the versions Lilia writes: EPUB 3.4 (the default file) and EPUB 2.0.1 (the
    /// "older readers" option), and EPUB Accessibility 1.2 for the accessibility metadata.</summary>
    public bool IsExportTarget { get; set; }

    /// <summary>Oldest first, so per-version tables read left to right.</summary>
    public int SortOrder { get; set; }

    public string? SpecUrl { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
