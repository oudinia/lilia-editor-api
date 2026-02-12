using System.Text.Json;

namespace Lilia.Core.Entities;

public class ImportReviewSession
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "in_progress"; // in_progress, imported, cancelled
    public JsonDocument? OriginalWarnings { get; set; }
    public Guid? DocumentId { get; set; }
    public JsonDocument? ParagraphTraces { get; set; }
    public string? SourceFilePath { get; set; }
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
}
