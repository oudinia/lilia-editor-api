using System.Text.Json;

namespace Lilia.Core.Entities;

public class ImportReviewSession
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "in_progress"; // in_progress, parsing, pending_review, auto_finalized, imported, cancelled
    public JsonDocument? OriginalWarnings { get; set; }
    public Guid? DocumentId { get; set; }
    public JsonDocument? ParagraphTraces { get; set; }
    public string? SourceFilePath { get; set; }
    public string? RawImportData { get; set; }
    public bool AutoFinalizeEnabled { get; set; } = false;
    public int? QualityScore { get; set; }
    public Guid? ProjectSessionId { get; set; } // Reserved for future multi-file project ingest layer
    // Document category unlocks specialised structural-finding rules. Null =
    // generic detection only. Values: "cv" | "thesis" | "report" | "research" | "business"
    public string? DocumentCategory { get; set; }

    /// <summary>
    /// Source format for the import — drives the route dispatcher to the
    /// correct format-specific review page. Values: "tex" | "project" |
    /// "docx" | "markdown" | "pdf". Defaults to "tex" for back-compat.
    /// </summary>
    public string SourceFormat { get; set; } = "tex";

    /// <summary>
    /// Per-tab progress in the aspect-based review UI. Shape:
    /// { structure: "unvisited|in_progress|done", content: ..., tables:
    /// ..., media, math, citations, coverage, diagnostics }. The tab is
    /// non-sequential — any order, any subset. The UI uses this to
    /// render the progress strip; Finalize is the only tab that enforces
    /// completeness (and only for errors, not warnings).
    /// </summary>
    public JsonDocument? TabProgress { get; set; }

    /// <summary>
    /// Last tab the user looked at — drives the "left off on Tables" hint
    /// in the Reviews list so returning users land back in context.
    /// </summary>
    public string? LastFocusedTab { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    // Navigation properties
    public virtual Job? Job { get; set; }
    public virtual User Owner { get; set; } = null!;
    public virtual Document? Document { get; set; }
    public virtual ICollection<ImportBlockReview> BlockReviews { get; set; } = new List<ImportBlockReview>();
    public virtual ICollection<ImportReviewCollaborator> Collaborators { get; set; } = new List<ImportReviewCollaborator>();
    public virtual ICollection<ImportBlockComment> Comments { get; set; } = new List<ImportBlockComment>();
    public virtual ICollection<ImportReviewActivity> Activities { get; set; } = new List<ImportReviewActivity>();
    public virtual ICollection<ImportDiagnostic> Diagnostics { get; set; } = new List<ImportDiagnostic>();
    public virtual ICollection<ImportStructuralFinding> StructuralFindings { get; set; } = new List<ImportStructuralFinding>();
}
