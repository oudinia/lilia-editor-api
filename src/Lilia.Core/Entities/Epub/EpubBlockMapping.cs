namespace Lilia.Core.Entities.Epub;

/// <summary>
/// How one Lilia block type (and variant) becomes EPUB, and back. One row per block type with the empty variant
/// is required (an integrity test holds every constant in <c>BlockTypes</c> to it); extra rows carry variants
/// (<c>heading/level-1</c>, <c>list/numbered</c>, <c>theorem/proof</c>, <c>equation/align</c>).
///
/// EPUB phase 1 (20 Nov 2026): today's exporter has its own hard-coded switch, so every fix made for the HTML
/// preview or the Word export never reached EPUB, and a missing case printed an empty paragraph. Here a block
/// type with no EPUB form says so as data: <see cref="Disposition"/> <c>drop</c> or <c>unsupported</c>, with a
/// <see cref="Reason"/>. The constructs a row writes are foreign keys, so a row can't name an element or an
/// <c>epub:type</c> the catalog doesn't know.
/// </summary>
public class EpubBlockMapping
{
    public Guid Id { get; set; }

    /// <summary>A <c>BlockTypes</c> constant (legacy aliases included).</summary>
    public string BlockType { get; set; } = string.Empty;

    /// <summary>Empty for the block type's default row; otherwise a variant key (<c>level-1</c>,
    /// <c>numbered</c>, <c>proof</c>, <c>align</c>, <c>verse</c>). Unique with <see cref="BlockType"/>.</summary>
    public string Variant { get; set; } = string.Empty;

    /// <summary>CHECK: render / drop (nothing in the book, on purpose) / unsupported (no EPUB form yet; the
    /// writer will show a visible placeholder and a warning, never an empty paragraph or a crash).</summary>
    public string Disposition { get; set; } = "render";

    /// <summary>The outer element written for the block (an xhtml-element construct).</summary>
    public Guid? ElementId { get; set; }

    /// <summary>The main element inside it, when there is one: <c>h1</c> in <c>section</c>, <c>math</c> in the
    /// equation's <c>div</c>, <c>img</c> in <c>figure</c>.</summary>
    public Guid? InnerElementId { get; set; }

    /// <summary>The <c>epub:type</c> on the outer element (a structural-semantic construct).</summary>
    public Guid? EpubTypeId { get; set; }

    /// <summary>The ARIA role on the outer element (an aria-role construct).</summary>
    public Guid? AriaRoleId { get; set; }

    public string? CssClass { get; set; }

    /// <summary>The C# writer that renders the row (phase 3 registers one per key). Required when
    /// <see cref="Disposition"/> is <c>render</c>; the integrity tests hold it to a known list.</summary>
    public string? TemplateKey { get; set; }

    /// <summary>JSON: which block fields fill what, e.g. <c>{"text":"inline","label":"id","caption":"figcaption"}</c>.</summary>
    public string? Slots { get; set; }

    /// <summary>CHECK: none / starts-file (the block opens a new XHTML file in the spine).</summary>
    public string FileSplit { get; set; } = "none";

    /// <summary>CHECK: none / toc-entry / landmark (the row's <c>epub:type</c> becomes a landmark) /
    /// page-list.</summary>
    public string Nav { get; set; } = "none";

    /// <summary>Manifest properties the block makes its file need (<c>mathml</c>, <c>svg</c>). Each must name a
    /// manifest-property construct.</summary>
    public List<string> ManifestProperties { get; set; } = new();

    /// <summary>The outer element in an EPUB 2.0.1 file, where XHTML 1.1 lacks the EPUB 3 one
    /// (<c>section</c>, <c>figure</c>, <c>aside</c> → <c>div</c>).</summary>
    public Guid? Epub2ElementId { get; set; }

    /// <summary>The EPUB 2 writer when it differs from <see cref="TemplateKey"/>: <c>equation-image</c> (no
    /// MathML), <c>endnote</c> (no pop-up notes), <c>drop</c>.</summary>
    public string? Epub2TemplateKey { get; set; }

    /// <summary>Oldest version the row may be written for (null: every version).</summary>
    public string? MinVersionSlug { get; set; }

    /// <summary>For the import: when several rows match an element, the highest wins.</summary>
    public int ImportPriority { get; set; }

    /// <summary>How far the catalog-driven writer handles the row. CHECK: full / partial / none / unsupported.
    /// Every row is <c>none</c> until phase 3 registers its handler.</summary>
    public string CoverageLevel { get; set; } = "none";

    /// <summary>Why a <c>drop</c> or <c>unsupported</c> row has no EPUB form (required for them by a CHECK).</summary>
    public string? Reason { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual EpubConstruct? Element { get; set; }
    public virtual EpubConstruct? InnerElement { get; set; }
    public virtual EpubConstruct? EpubType { get; set; }
    public virtual EpubConstruct? AriaRole { get; set; }
    public virtual EpubConstruct? Epub2Element { get; set; }
    public virtual EpubSpecVersion? MinVersion { get; set; }
}
