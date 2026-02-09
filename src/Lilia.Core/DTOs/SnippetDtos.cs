namespace Lilia.Core.DTOs;

public record SnippetDto(
    Guid Id,
    string Name,
    string? Description,
    string LatexContent,
    string BlockType,
    string Category,
    List<string> RequiredPackages,
    string? Preamble,
    List<string> Tags,
    bool IsFavorite,
    bool IsSystem,
    int UsageCount,
    string? UserId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateSnippetDto(
    string Name,
    string LatexContent,
    string BlockType,
    string Category,
    string? Description = null,
    List<string>? RequiredPackages = null,
    string? Preamble = null,
    List<string>? Tags = null
);

public record UpdateSnippetDto(
    string? Name = null,
    string? Description = null,
    string? LatexContent = null,
    string? BlockType = null,
    string? Category = null,
    List<string>? RequiredPackages = null,
    string? Preamble = null,
    List<string>? Tags = null
);

public record SnippetSearchDto(
    string? Query = null,
    string? Category = null,
    bool? FavoritesOnly = null,
    bool IncludeSystem = true,
    int Page = 1,
    int PageSize = 50
);

public record SnippetPageDto(
    List<SnippetDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);
