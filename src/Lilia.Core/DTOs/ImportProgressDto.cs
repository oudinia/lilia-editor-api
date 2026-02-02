namespace Lilia.Core.DTOs;

/// <summary>
/// Rich progress data for document import operations.
/// </summary>
public record ImportProgressDto
{
    /// <summary>
    /// Unique job identifier for this import.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Current processing phase.
    /// </summary>
    public required ImportPhase Phase { get; init; }

    /// <summary>
    /// Overall progress percentage (0-100).
    /// </summary>
    public required int Progress { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Detailed description of current activity.
    /// </summary>
    public string? CurrentActivity { get; init; }

    // Document structure info
    public int? TotalPages { get; init; }
    public int? CurrentPage { get; init; }
    public int? TotalBlocks { get; init; }
    public int? ProcessedBlocks { get; init; }

    // Block type counts discovered so far
    public Dictionary<string, int>? BlockCounts { get; init; }

    // Sections/chapters found
    public List<string>? Sections { get; init; }
    public string? CurrentSection { get; init; }

    // Issues found during processing
    public List<ImportWarningDto>? Warnings { get; init; }
    public int WarningCount { get; init; }
    public int ErrorCount { get; init; }

    // Timing information
    public TimeSpan Elapsed { get; init; }
    public TimeSpan? EstimatedRemaining { get; init; }

    // Statistics
    public long? FileSizeBytes { get; init; }
    public int? ImageCount { get; init; }
    public int? TableCount { get; init; }
    public int? EquationCount { get; init; }
}

/// <summary>
/// Import processing phases.
/// </summary>
public enum ImportPhase
{
    /// <summary>Receiving and validating file.</summary>
    Receiving,

    /// <summary>Parsing document structure.</summary>
    Parsing,

    /// <summary>Extracting pages and sections.</summary>
    ExtractingPages,

    /// <summary>Processing images.</summary>
    ProcessingImages,

    /// <summary>Processing tables.</summary>
    ProcessingTables,

    /// <summary>Converting equations.</summary>
    ConvertingEquations,

    /// <summary>Converting blocks to editor format.</summary>
    ConvertingBlocks,

    /// <summary>Validating content.</summary>
    Validating,

    /// <summary>Saving to database.</summary>
    Saving,

    /// <summary>Import completed successfully.</summary>
    Completed,

    /// <summary>Import failed.</summary>
    Failed
}

/// <summary>
/// Warning encountered during import.
/// </summary>
public record ImportWarningDto
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? BlockType { get; init; }
    public int? PageNumber { get; init; }
    public string? Details { get; init; }
}

/// <summary>
/// Final import result sent when complete.
/// </summary>
public record ImportCompletedDto
{
    public required string JobId { get; init; }
    public required bool Success { get; init; }
    public string? DocumentId { get; init; }
    public string? DocumentTitle { get; init; }

    // Final statistics
    public int TotalBlocks { get; init; }
    public int TotalPages { get; init; }
    public Dictionary<string, int>? BlockCounts { get; init; }
    public List<string>? Sections { get; init; }

    // Timing
    public TimeSpan TotalDuration { get; init; }

    // Issues
    public List<ImportWarningDto>? Warnings { get; init; }
    public string? ErrorMessage { get; init; }
}
