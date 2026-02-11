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

    // Navigation properties
    public virtual ImportReviewSession Session { get; set; } = null!;
    public virtual User? Reviewer { get; set; }
}
