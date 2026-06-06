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
    string? DocumentCategory = null,
    // Optimistic-concurrency version. The Flow editor carries this into
    // every background sync; a stale value yields 409.
    // See 2026-05-21-flow-editor-save-model.md.
    int Version = 0,
    // User-authored custom preamble (macros / environments) emitted verbatim
    // into the export preamble. See Document.CustomPreamble.
    string? CustomPreamble = null
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
    string? Orientation,
    // User-authored custom preamble (macros / environments). See
    // Document.CustomPreamble.
    string? CustomPreamble = null
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
    bool IsPublic,
    // Iter 8 — both optional so existing callers keep working.
    // Null Permission falls back to the doc's current value (or
    // "view" on first enable). Null ExpiresAt means "no change" —
    // omitting the field doesn't clear an existing expiry. Send
    // ClearExpiry=true to revert to "never".
    string? Permission = null,
    DateTime? ExpiresAt = null,
    bool ClearExpiry = false
);

// PUT /documents/{id}/team payload. Null TeamId means detach. The
// plain UpdateDocumentDto path can't carry team attachment because
// it never had a TeamId field (and adding one re-introduces the
// JSON "absent vs explicit null" problem). A dedicated DTO and
// endpoint sidesteps that and matches the spec verbs Attach/Detach.
public record SetDocumentTeamDto(
    Guid? TeamId
);

public record DocumentShareResultDto(
    string ShareLink,
    string? ShareSlug,
    bool IsPublic,
    DateTime? LinkExpiresAt = null,
    string LinkPermission = "view"
);
