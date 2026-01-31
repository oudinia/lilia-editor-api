using System.Text.Json;

namespace Lilia.Core.DTOs;

public record TemplateListDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    string? Thumbnail,
    bool IsPublic,
    bool IsSystem,
    int UsageCount,
    string? UserId,
    string? UserName,
    DateTime CreatedAt
);

public record TemplateDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    string? Thumbnail,
    JsonElement Content,
    bool IsPublic,
    bool IsSystem,
    int UsageCount,
    string? UserId,
    string? UserName,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateTemplateDto(
    Guid DocumentId,
    string Name,
    string? Description,
    string? Category,
    bool IsPublic
);

public record UpdateTemplateDto(
    string? Name,
    string? Description,
    string? Category,
    bool? IsPublic
);

public record UseTemplateDto(
    string? Title
);

public record TemplateCategoryDto(
    string Name,
    int Count
);
