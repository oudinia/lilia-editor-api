using System.Text.Json;

namespace Lilia.Core.Entities;

public class ImportBlockReview
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int BlockIndex { get; set; }
    public string BlockId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending, approved, rejected, edited
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public JsonDocument OriginalContent { get; set; } = JsonDocument.Parse("{}");
    public string OriginalType { get; set; } = string.Empty;
    public JsonDocument? CurrentContent { get; set; }
    public string? CurrentType { get; set; }
    public int? Confidence { get; set; }
    public JsonDocument? Warnings { get; set; }
    public int SortOrder { get; set; }
    public int Depth { get; set; }

    /// <summary>
    /// Byte offsets into the owning session's RawImportData where this
    /// block's LaTeX lives — powers the "Source" sub-tab on the .tex
    /// redesign. Shape: { start: int, end: int }. Nullable — parser
    /// populates best-effort during staging; the endpoint falls back to
    /// RenderService.RenderBlockToLatex() when unset.
    /// </summary>
    public JsonDocument? SourceRange { get; set; }

    /// <summary>
    /// Overleaf / multi-file project imports — relative path of the
    /// .tex file this block originated from. Null for single-file
    /// imports. Drives per-file grouping in the tree view.
    /// </summary>
    public string? SourceFile { get; set; }

    // Navigation properties
    public virtual ImportReviewSession Session { get; set; } = null!;
    public virtual User? Reviewer { get; set; }
}
