using System.Text.Json;

namespace Lilia.Core.Entities;

public class Job
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
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

    /// <summary>
    /// One chunk of a bulk-imported PDF (bulk import, 21 Nov 2026): the job that carries the chunk's LaTeX import (its zip at
    /// <c>imports/{jobId}.zip</c>, its review session). Never an import of its own: the plan's monthly imports, the plan
    /// meter, recent activity and the admin's stats count <see cref="Import"/> only, and the bulk file is that one
    /// import (a job with the file's id). Its own type rather than a flag, so every reader that counts or lists imports
    /// (they all test <c>== "IMPORT"</c>) leaves chunks out without being changed, and none can be missed.
    /// </summary>
    public const string ImportChunk = "IMPORT_CHUNK";
    public const string Export = "EXPORT";
    public const string Convert = "CONVERT";
}
