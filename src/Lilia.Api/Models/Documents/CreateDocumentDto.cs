namespace Lilia.Api.Models.Documents;

/// <summary>
/// Request body for POST /api/documents.
///
/// LILIA-121 (documentclass-first): the dialog now asks "what are you writing?"
/// up front, so the create payload carries the document class plus the universal
/// LaTeX class options (paper size, font size, columns, sides, title page,
/// orientation). Field names + casing must stay in lock-step with the
/// TypeScript shape <c>CreateDocumentRequest</c> in
/// <c>lilia-web-editor/src/types/documentClass.ts</c>; see the shared contract
/// at <c>lilia-docs/teams/2026-05-06-documentclass-first/01-shared-contract.md</c>.
///
/// Append-only: do not rename or remove fields without a coordinated change
/// across both repos.
/// </summary>
public class CreateDocumentDto
{
    public string Title { get; set; } = "Untitled";
    public string Language { get; set; } = "en";

    /// <summary>
    /// High-level category — "article" | "book" | "report" | "cv" today.
    /// Used for default-class lookup and StructuralFindingService rules.
    /// </summary>
    public string DocumentCategory { get; set; } = "article";

    /// <summary>
    /// The actual LaTeX \documentclass — "article", "book", "moderncv", etc.
    /// Looked up against the seeded latex_document_classes table at create
    /// time to pull the default engine + required packages.
    /// </summary>
    public string DocumentClass { get; set; } = "article";

    // ── Universal class options (B2 grid) ─────────────────────────────
    // Each maps either to a structured Document column (PaperSize, FontSize,
    // Columns) or contributes a token to LatexDocumentClassOptions. We never
    // double-emit — a value goes into ONE place. See the shared contract for
    // the full mapping table.

    /// <summary>"a4" | "letter" | "a5"</summary>
    public string PaperSize { get; set; } = "a4";

    /// <summary>10 | 11 | 12</summary>
    public int FontSize { get; set; } = 11;

    /// <summary>1 | 2</summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// When Columns == 2 and BalancedColumns is true the export builder uses
    /// the multicol package (handled by Team 1) instead of the "twocolumn"
    /// class option. Off by default.
    /// </summary>
    public bool BalancedColumns { get; set; } = false;

    /// <summary>"one" | "two" — emits "twoside" when "two".</summary>
    public string Sides { get; set; } = "one";

    /// <summary>false = inline (\maketitle), true = "titlepage" environment.</summary>
    public bool TitlePage { get; set; } = false;

    /// <summary>"portrait" | "landscape" — emits "landscape" when non-default.</summary>
    public string Orientation { get; set; } = "portrait";

    public Guid? TeamId { get; set; }
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// Legacy field — kept on the DTO until the editor stops sending it.
    /// New paths should prefer the structured options above; the existing
    /// <c>FontFamily</c> column on Document is still respected when this is
    /// non-null (templates and starter clones still copy it).
    /// </summary>
    public string? FontFamily { get; set; }
}
