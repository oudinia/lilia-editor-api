namespace Lilia.Core.Entities.Epub;

/// <summary>
/// How one package metadata field is written for one target version, and where its value comes from:
/// <c>dc:language</c> is required in every version and comes from the document's language;
/// <c>schema:accessModeSufficient</c> is required by EPUB Accessibility 1.2 and is computed from the
/// alt-text coverage.
///
/// EPUB phase 1 (20 Nov 2026): today's file declares every book English (the web app sends only the title),
/// makes a new identifier on every export and writes no accessibility metadata. These rows say what each target
/// needs; the writer reads them in phase 3.
/// </summary>
public class EpubMetadataRule
{
    public Guid Id { get; set; }

    /// <summary>A package-metadata or a11y-property construct.</summary>
    public Guid ConstructId { get; set; }

    /// <summary>The target the rule is for. Unique with <see cref="ConstructId"/>.</summary>
    public string VersionSlug { get; set; } = string.Empty;

    /// <summary>CHECK: required / recommended / optional.</summary>
    public string Requirement { get; set; } = "optional";

    /// <summary>Where the value comes from: <c>document.title</c>, <c>document.language</c>,
    /// <c>document.epub_identifier</c>, <c>computed:alt_coverage</c>…</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>How the version writes it, e.g. <c>&lt;meta property="dcterms:modified"&gt;</c> in EPUB 3,
    /// <c>&lt;dc:date opf:event="modification"&gt;</c> in EPUB 2.</summary>
    public string? Expression { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual EpubConstruct? Construct { get; set; }
    public virtual EpubSpecVersion? Version { get; set; }
}
