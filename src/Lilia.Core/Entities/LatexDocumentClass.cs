namespace Lilia.Core.Entities;

/// <summary>
/// A LaTeX \documentclass{} entry. Controls how the parser sets up the
/// initial frame for a document: default engine (pdflatex / xelatex),
/// auto-loaded packages, and which shim (moderncv, altacv, …) to apply
/// at import time. Seeded with the top 10 common classes.
/// </summary>
public class LatexDocumentClass
{
    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// CHECK: cv / article / report / book / presentation / letter / memoir / other.
    /// Drives the "specialised findings" rules in StructuralFindingService.
    /// </summary>
    public string Category { get; set; } = "other";

    public string CoverageLevel { get; set; } = "none";

    /// <summary>pdflatex / xelatex / lualatex — used by Document.LatexEngine on import.</summary>
    public string? DefaultEngine { get; set; }

    /// <summary>Packages auto-loaded by this class, stored as a JSON array of slugs.</summary>
    public string? RequiredPackages { get; set; }

    /// <summary>Name of the shim service, if we remap this class at import (moderncv, altacv, resume).</summary>
    public string? ShimName { get; set; }

    /// <summary>
    /// LILIA-121 D1 — JSON array of sectioning block keys this class supports.
    /// Read by <c>BlockTypesController.GetBlockTypes(?docId=...)</c> to filter
    /// the editor's slash menu / ⌘K palette / insertions panel so users can't
    /// insert <c>\chapter</c> in an article doc (pdflatex error at compile)
    /// or <c>\frontmatter</c> outside of book/memoir.
    ///
    /// Stored as a JSON string array of canonical sectioning slugs:
    ///   "part", "chapter", "section", "subsection", "subsubsection",
    ///   "paragraph", "subparagraph", "frontmatter", "mainmatter",
    ///   "backmatter", "appendix"
    ///
    /// Non-sectioning block types (paragraph, list, equation, figure, table,
    /// code, blockquote, theorem, abstract, bibliography, tableOfContents,
    /// pageBreak) are always allowed in every class and not enumerated here.
    /// Null = catalog row pre-dates the column and the editor should fall
    /// back to "everything allowed" until the next reseed.
    /// </summary>
    public string? AllowedBlocks { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
