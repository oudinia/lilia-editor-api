using System.Text.Json;

namespace Lilia.Core.DTOs;

/// <summary>
/// Paginated result wrapper for API responses
/// </summary>
public record PaginatedResult<T>(
    List<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

public record DocumentListDto(
    Guid Id,
    string Title,
    string OwnerId,
    string? OwnerName,
    Guid? TeamId,
    string? TeamName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastOpenedAt,
    int BlockCount,
    int SectionCount,
    List<OutlineItemDto> Outline,
    List<LabelDto> Labels
);

public record OutlineItemDto(
    string Title,
    int Level
);

public record DocumentDto(
    Guid Id,
    string Title,
    string OwnerId,
    Guid? TeamId,
    string Language,
    string PaperSize,
    string FontFamily,
    int FontSize,
    int Columns,
    string ColumnSeparator,
    double ColumnGap,
    bool IsPublic,
    string? ShareLink,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastOpenedAt,
    string? MarginTop,
    string? MarginBottom,
    string? MarginLeft,
    string? MarginRight,
    string? HeaderText,
    string? FooterText,
    double? LineSpacing,
    string? ParagraphIndent,
    string? PageNumbering,
    List<BlockDto> Blocks,
    List<BibliographyEntryDto> Bibliography,
    List<LabelDto> Labels
);

public record CreateDocumentDto(
    string? Title,
    Guid? TeamId,
    string? Language,
    string? PaperSize,
    string? FontFamily,
    int? FontSize,
    Guid? TemplateId
);

public record UpdateDocumentDto(
    string? Title,
    string? Language,
    string? PaperSize,
    string? FontFamily,
    int? FontSize,
    int? Columns,
    string? ColumnSeparator,
    double? ColumnGap,
    string? MarginTop,
    string? MarginBottom,
    string? MarginLeft,
    string? MarginRight,
    string? HeaderText,
    string? FooterText,
    double? LineSpacing,
    string? ParagraphIndent,
    string? PageNumbering
);

public record ShareDocumentDto(
    bool IsPublic
);

public record DocumentShareResultDto(
    string ShareLink,
    bool IsPublic
);
