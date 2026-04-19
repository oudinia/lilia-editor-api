using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Structural finding surfaced during import review — "this paragraph looks
/// like a heading", "this table should be split", "unused TOC block at top".
/// Distinct from ImportDiagnostic (parser problems); these are suggestions
/// about document structure that the user accepts, dismisses, or ignores.
///
/// One row is owned by either a SessionId OR a DocumentId — both never set,
/// but at least one is.
/// </summary>
public class ImportStructuralFinding
{
    public Guid Id { get; set; }

    // Exactly one of these two is populated. Sessions for in-progress
    // imports; documents for hints applied to already-finalised imports
    // (so users can still fix structure post hoc).
    public Guid? SessionId { get; set; }
    public Guid? DocumentId { get; set; }

    // Null = session/document-level finding (e.g. "use moderncv class").
    // Non-null references ImportBlockReview.BlockId (for sessions) or
    // Block.Id.ToString() (for documents).
    public string? BlockId { get; set; }

    // Kind of finding. Enforced by CHECK constraint in the DB.
    public string Kind { get; set; } = string.Empty;

    // Severity parallels ImportDiagnostic but leans informational: most
    // findings are "hint" severity — advisory, not blocking.
    public string Severity { get; set; } = "hint";   // hint | warning | critical

    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;

    // How the UI applies this finding. One of:
    //   convert_block_type | set_document_class | delete_block |
    //   split_header_table | open_edit_modal | merge_list
    public string ActionKind { get; set; } = string.Empty;
    public JsonDocument? ActionPayload { get; set; }

    // Lifecycle.
    public string Status { get; set; } = "pending";   // pending | applied | dismissed
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation (both nullable because of the XOR). EF treats each as an
    // optional relationship; at runtime exactly one should be set.
    public virtual ImportReviewSession? Session { get; set; }
    public virtual Document? Document { get; set; }
}
