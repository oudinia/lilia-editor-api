using System.Text.Json;

namespace Lilia.Core.DTOs;

public record JobDto(
    Guid Id,
    string UserId,
    Guid? DocumentId,
    string? DocumentTitle,
    string JobType,
    string Status,
    int Progress,
    string? SourceFormat,
    string? TargetFormat,
    string? SourceFileName,
    string? ResultFileName,
    string? ResultUrl,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt
);

public record JobListDto(
    Guid Id,
    Guid? DocumentId,
    string? DocumentTitle,
    string JobType,
    string Status,
    int Progress,
    string? SourceFormat,
    string? TargetFormat,
    string? SourceFileName,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

public record CreateExportJobDto(
    Guid DocumentId,
    string Format,
    Dictionary<string, object>? Options = null
);

public record CreateImportJobDto(
    string FileName,
    string Format,
    string? Title = null
);

public record ExportResultDto(
    Guid JobId,
    string Status,
    string Content,
    string Filename
);

public record ImportOptionsDto(
    bool PreserveFormatting = true,
    bool ImportImages = true,
    bool ImportBibliography = true,
    bool AutoDetectEquations = true,
    bool SplitByHeadings = true
);

public record ImportRequestDto(
    string Content,      // Base64 encoded for DOCX, plain text for LaTeX/LML
    string Format,       // "DOCX", "LATEX", "LML"
    string Filename,
    string? Title = null,
    ImportOptionsDto? Options = null
);

public record ImportResultDto(
    JobDto Job,
    ImportedDocumentInfoDto? Document,
    Guid? ReviewSessionId = null
);

public record ImportedDocumentInfoDto(
    Guid Id,
    string Title,
    DateTime CreatedAt
);
