namespace Lilia.Core.Models.Epub;

public record EpubAnalysisResult(
    EpubMetadata Metadata,
    List<EpubIssue> Issues,
    int ChapterCount,
    int BlockCount,
    int ImageCount,
    long FileSizeBytes
);

public record EpubIssue(
    string Category,   // "metadata", "structure", "formatting", "accessibility", "encoding"
    string Severity,   // "error", "warning", "info"
    string Description,
    string? Location = null
);
