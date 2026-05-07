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
    List<LabelDto> Labels,
    string? Role = null,
    int ValidationErrorCount = 0,
    int ValidationWarningCount = 0,
    DateTime? ValidationCheckedAt = null
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
    string? ShareSlug,
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
    string? LatexDocumentClass,
    string? LatexDocumentClassOptions,
    string? LatexPackages,
    bool BalancedColumns,
    List<BlockDto> Blocks,
    List<BibliographyEntryDto> Bibliography,
    List<LabelDto> Labels,
    bool AiEnabled = false,
    string LatexEngine = "pdflatex",
    bool ExperimentalLatexEdit = false,
    // Documentclass-first read-back (LILIA-131). The editor's stamp + popover
    // need the category in addition to the LaTeX class string.
    string? DocumentCategory = null
);

// CreateDocumentDto moved to Lilia.Api.Models.Documents.CreateDocumentDto as
// part of LILIA-121 (documentclass-first). The DTO now carries the document
// class + universal class options (paper size, font size, columns, sides,
// title page, orientation) so the create dialog can publish a fully-formed
// preamble in a single round-trip. See:
//   src/Lilia.Api/Models/Documents/CreateDocumentDto.cs
//   lilia-docs/teams/2026-05-06-documentclass-first/01-shared-contract.md

public record UpdateDocumentDto(
    string? Title,
    string? Language,
    string? PaperSize,
    string? FontFamily,
    int? FontSize,
    int? Columns,
    string? ColumnSeparator,
    double? ColumnGap,
    string? LatexDocumentClass,
    string? LatexDocumentClassOptions,
    string? LatexPackages,
    bool? BalancedColumns,
    string? MarginTop,
    string? MarginBottom,
    string? MarginLeft,
    string? MarginRight,
    string? HeaderText,
    string? FooterText,
    double? LineSpacing,
    string? ParagraphIndent,
    string? PageNumbering,
    bool? AiEnabled,
    string? LatexEngine,
    bool? ExperimentalLatexEdit,
    // Documentclass-first popover fields (LILIA-131). Mirror CreateDocumentDto's
    // flat shape so the editor's DocumentStamp popover can persist class +
    // category + the three options that aren't structured columns (sides,
    // titlePage, orientation — encoded into LatexDocumentClassOptions).
    string? DocumentClass,
    string? DocumentCategory,
    string? Sides,
    bool? TitlePage,
    string? Orientation
);

public record TrashDocumentDto(
    Guid Id,
    string Title,
    string OwnerId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime DeletedAt,
    int DaysUntilPurge
);

public record ShareDocumentDto(
    bool IsPublic
);

public record DocumentShareResultDto(
    string ShareLink,
    string? ShareSlug,
    bool IsPublic
);
