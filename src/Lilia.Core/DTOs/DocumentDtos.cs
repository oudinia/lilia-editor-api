using System.Text.Json;

namespace Lilia.Core.DTOs;

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
    List<LabelDto> Labels
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
    bool IsPublic,
    string? ShareLink,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastOpenedAt,
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
    int? FontSize
);

public record ShareDocumentDto(
    bool IsPublic
);

public record DocumentShareResultDto(
    string ShareLink,
    bool IsPublic
);
