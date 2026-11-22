namespace Lilia.Core.Entities.Epub;

/// <summary>
/// One inline mark, both ways: the LML mark (<c>**bold**</c>, <c>$x$</c>, <c>@cite{key}</c>) and the XHTML
/// that carries it (<c>strong</c>, <c>math</c>, <c>a role="doc-biblioref"</c>), with the forms an imported
/// book may use for the same thing (<c>b</c> for bold).
///
/// EPUB phase 1 (20 Nov 2026): the audit found every inline mark printed as source in today's EPUB
/// (asterisks, dollar signs, backslashes) and lost on import. The marks are those Lilia's LML emitter writes
/// (lilia-docs/features/lml-specification.md, "Inline Elements").
/// </summary>
public class EpubInlineMapping
{
    public Guid Id { get; set; }

    /// <summary>Stable key: <c>bold</c>, <c>italic</c>, <c>math</c>, <c>cite</c>, <c>footnote</c>…</summary>
    public string MarkKey { get; set; } = string.Empty;

    /// <summary>The LML syntax, as the spec writes it.</summary>
    public string LmlSyntax { get; set; } = string.Empty;

    /// <summary>CHECK: both / export / import.</summary>
    public string Direction { get; set; } = "both";

    /// <summary>The element written for the mark (an xhtml-element construct).</summary>
    public Guid ElementId { get; set; }

    public Guid? EpubTypeId { get; set; }
    public Guid? AriaRoleId { get; set; }

    /// <summary>Attributes the writer sets, as a hint for the handler (<c>href="#bib-{key}"</c>).</summary>
    public string? Attributes { get; set; }

    /// <summary>What an imported book may use for the same mark: <c>b</c>, <c>span[style*=italic]</c>.</summary>
    public List<string> ImportSynonyms { get; set; } = new();

    /// <summary>Manifest properties the mark makes its file need (<c>mathml</c>).</summary>
    public List<string> ManifestProperties { get; set; } = new();

    /// <summary>The element in an EPUB 2.0.1 file, where XHTML 1.1 lacks the EPUB 3 one (<c>u</c>, <c>s</c>,
    /// <c>mark</c>, <c>math</c>).</summary>
    public Guid? Epub2ElementId { get; set; }

    /// <summary>A note on the EPUB 2 form (<c>span class="underline"</c>, an equation picture).</summary>
    public string? Epub2Form { get; set; }

    public string? TemplateKey { get; set; }

    /// <summary>CHECK: full / partial / none / unsupported. <c>none</c> until phases 2 and 3.</summary>
    public string CoverageLevel { get; set; } = "none";

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual EpubConstruct? Element { get; set; }
    public virtual EpubConstruct? EpubType { get; set; }
    public virtual EpubConstruct? AriaRole { get; set; }
    public virtual EpubConstruct? Epub2Element { get; set; }
}
