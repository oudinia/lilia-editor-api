using System.Text.Json;

namespace Lilia.Core.Entities;

public class Job
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public string JobType { get; set; } = string.Empty; // IMPORT, EXPORT
    public string Status { get; set; } = JobStatus.Pending; // PENDING, PROCESSING, COMPLETED, FAILED, CANCELLED
    public int Progress { get; set; } = 0;
    public string? SourceFormat { get; set; } // docx, pdf, latex, etc.
    public string? TargetFormat { get; set; } // docx, pdf, latex, html, markdown, lml
    public string? SourceFileName { get; set; }
    public string? ResultFileName { get; set; }
    public string? ResultUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Direction { get; set; } // INBOUND, OUTBOUND
    public string? InputFileKey { get; set; }
    public long? InputFileSize { get; set; }
    public string? OutputFileKey { get; set; }
    public JsonDocument? Options { get; set; }
    public JsonDocument? ErrorDetails { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime? StartedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public JsonDocument? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public virtual User? User { get; set; }
    public virtual Document? Document { get; set; }
}

public static class JobStatus
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

public static class JobTypes
{
    public const string Import = "IMPORT";
    public const string Export = "EXPORT";
    public const string Convert = "CONVERT";
}
