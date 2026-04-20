namespace Lilia.Core.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public string Title { get; set; } = "Untitled";
    public string Language { get; set; } = "en";
    public string PaperSize { get; set; } = "a4";
    public string FontFamily { get; set; } = "serif";
    public int FontSize { get; set; } = 12;
    public int Columns { get; set; } = 1;
    public string ColumnSeparator { get; set; } = "none";
    public double ColumnGap { get; set; } = 1.5;
    public bool IsPublic { get; set; }
    public string? ShareLink { get; set; }
    public string? ShareSlug { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastOpenedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string Status { get; set; } = "draft"; // draft, saved, published
    public DateTime? LastAutoSavedAt { get; set; }

    // Layout settings
    public string? MarginTop { get; set; }
    public string? MarginBottom { get; set; }
    public string? MarginLeft { get; set; }
    public string? MarginRight { get; set; }
    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }
    public double? LineSpacing { get; set; }
    public string? ParagraphIndent { get; set; }
    public string? PageNumbering { get; set; } // "arabic", "roman", "none"

    // Template fields — templates are documents with is_template = true
    public bool IsTemplate { get; set; }
    public string? TemplateName { get; set; }
    public string? TemplateDescription { get; set; }
    public string? TemplateCategory { get; set; }
    public string? TemplateThumbnail { get; set; }
    public bool IsPublicTemplate { get; set; }
    public int TemplateUsageCount { get; set; }

    // Help content fields — help articles are documents with is_help_content = true
    public bool IsHelpContent { get; set; }
    public string? HelpCategory { get; set; }
    public int HelpOrder { get; set; }
    public string? HelpSlug { get; set; }
    public string? SearchText { get; set; }

    // Validation summary — populated by the validate endpoint, shown as badge in the document list
    public int ValidationErrorCount { get; set; }
    public int ValidationWarningCount { get; set; }
    public DateTime? ValidationCheckedAt { get; set; }

    // LaTeX preamble preserved from import, used by the LaTeX exporter so the
    // round-trip produces a document that compiles under the original class.
    // LatexPackages is a JSON array of { name: string, options?: string }.
    public string? LatexDocumentClass { get; set; }
    public string? LatexDocumentClassOptions { get; set; }
    public string? LatexPackages { get; set; }

    /// Multi-column balancing — maps to multicol's auto-balance behaviour in
    /// LaTeX, w:cols w:equalWidth in DOCX, and column-fill:balance in HTML.
    public bool BalancedColumns { get; set; }

    /// Document category unlocks specialised structural-finding rules and
    /// guides LaTeX class selection. Null = generic. Values:
    /// "cv" | "thesis" | "report" | "research" | "business".
    public string? DocumentCategory { get; set; }

    // Per-document AI opt-in. Default false; the user explicitly toggles
    // this on per doc before any AI feature fires. The orchestrator checks
    // this + any future org/user-level gate via a simple AND.
    public bool AiEnabled { get; set; }

    // Compile engine for this document. pdflatex is the default and
    // handles the common case; xelatex / lualatex are auto-selected at
    // import time when the source relies on fontspec / \setmainfont or
    // similar XeTeX-only primitives. Users can later expose this in a
    // settings UI if they want to force a specific engine. CHECK-constrained
    // at the DB layer to the three supported values.
    public string LatexEngine { get; set; } = "pdflatex";

    // Opt-in to the "edit block as LaTeX" surface (v2 round-trip). Default
    // false. When true, BlockWrapper shows a LaTeX tab for supported block
    // types and POST /api/blocks/{id}/from-latex accepts writes. Gated
    // per-doc because the fragment→block reverse parse is lossy for rich
    // types (tables, nested figure content) and we don't want silent
    // corruption — users explicitly opt in.
    public bool ExperimentalLatexEdit { get; set; }

    // Navigation properties
    public virtual User Owner { get; set; } = null!;
    public virtual Team? Team { get; set; }
    public virtual ICollection<Block> Blocks { get; set; } = new List<Block>();
    public virtual ICollection<BibliographyEntry> BibliographyEntries { get; set; } = new List<BibliographyEntry>();
    public virtual ICollection<DocumentLabel> DocumentLabels { get; set; } = new List<DocumentLabel>();
    public virtual ICollection<DocumentCollaborator> Collaborators { get; set; } = new List<DocumentCollaborator>();
    public virtual ICollection<DocumentGroup> DocumentGroups { get; set; } = new List<DocumentGroup>();
    public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public virtual ICollection<DocumentSnapshot> Snapshots { get; set; } = new List<DocumentSnapshot>();
}
