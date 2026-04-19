namespace Lilia.Core.Entities;

public class ImportDiagnostic
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }

    // Null = session-scoped (preamble, documentclass). Non-null matches ImportBlockReview.BlockId.
    public string? BlockId { get; set; }

    // Sub-block precision, e.g. "paragraph/cite[1]". Null when block-scoped.
    public string? ElementPath { get; set; }

    public int? SourceLineStart { get; set; }
    public int? SourceLineEnd { get; set; }
    public int? SourceColStart { get; set; }
    public int? SourceColEnd { get; set; }
    public string? SourceSnippet { get; set; }

    // Closed vocabulary enforced by CHECK constraint (see LiliaDbContext)
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";

    // Stable identifier for analytics + doc deeplinks, e.g. LATEX.UNSUPPORTED_CLASS.BEAMER
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SuggestedAction { get; set; }
    public bool AutoFixApplied { get; set; }
    public string? DocsUrl { get; set; }

    public bool Dismissed { get; set; }
    public string? DismissedBy { get; set; }
    public DateTime? DismissedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ImportReviewSession Session { get; set; } = null!;
}
