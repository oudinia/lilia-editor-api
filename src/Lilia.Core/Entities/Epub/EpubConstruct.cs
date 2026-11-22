namespace Lilia.Core.Entities.Epub;

/// <summary>
/// One EPUB construct, whatever its kind: a package metadata field (<c>dc:language</c>), a manifest property
/// (<c>mathml</c>), a spine property (<c>page-progression-direction</c>), a navigation type (<c>landmarks</c>),
/// an <c>epub:type</c> value (<c>footnote</c>), an ARIA role (<c>doc-noteref</c>), an XHTML element
/// (<c>figure</c>), an accessibility property (<c>schema:accessModeSufficient</c>), a media type
/// (<c>image/avif</c>) or a rendition property. Which versions have it, and in what state, is in
/// <see cref="Support"/>.
///
/// EPUB phase 1 (20 Nov 2026): the rule kept from the LaTeX catalog is that the database holds facts and
/// routing, never code. Block and inline mappings point at these rows, so an integrity test can refuse a
/// mapping that would write something the target version doesn't have, or has deprecated.
/// </summary>
public class EpubConstruct
{
    public Guid Id { get; set; }

    /// <summary>CHECK: package-metadata / manifest-property / spine-property / nav-type / structural-semantic /
    /// aria-role / xhtml-element / xhtml-attribute / a11y-property / media-type / rendition-property.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The name as written in the file: <c>dc:title</c>, <c>cover-image</c>, <c>footnote</c>,
    /// <c>doc-footnote</c>, <c>section</c>, <c>image/png</c>. Unique with <see cref="Kind"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>How widely reading systems honour it. CHECK: core / wide / partial / rare.</summary>
    public string ReaderSupport { get; set; } = "wide";

    /// <summary>What a writer does for readers without it. CHECK: none / alttext / altimg / text / div / span /
    /// drop / convert.</summary>
    public string Fallback { get; set; } = "none";

    /// <summary>How an EPUB 2.0.1 file says the same thing, when it can (<c>toc</c> → NCX <c>navMap</c>,
    /// <c>landmarks</c> → <c>guide</c>, <c>cover-image</c> → <c>meta name="cover"</c>). Null when EPUB 2 has
    /// the construct as is, or nothing equivalent.</summary>
    public string? Epub2Form { get; set; }

    /// <summary>For an <c>epub:type</c> value, the DPUB-ARIA role that says the same thing, when one exists
    /// and isn't deprecated (<c>footnote</c> → <c>doc-footnote</c>). Must name an aria-role construct.</summary>
    public string? PairedAriaRole { get; set; }

    /// <summary>How far Lilia's import handles it, as for LaTeX tokens. CHECK: full / partial / none /
    /// unsupported. Everything is <c>none</c> until the import reads the catalog (phase 2); an import meeting
    /// an unknown construct will add it as <c>unsupported</c>.</summary>
    public string CoverageLevel { get; set; } = "none";

    public string? SpecUrl { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<EpubConstructSupport> Support { get; set; } = new List<EpubConstructSupport>();
}
