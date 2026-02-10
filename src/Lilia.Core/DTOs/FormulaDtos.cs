namespace Lilia.Core.DTOs;

public record FormulaDto(
    Guid Id,
    string Name,
    string? Description,
    string LatexContent,
    string? LmlContent,
    string Category,
    string? Subcategory,
    List<string> Tags,
    bool IsFavorite,
    bool IsSystem,
    int UsageCount,
    string? UserId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateFormulaDto(
    string Name,
    string LatexContent,
    string Category,
    string? Description = null,
    string? Subcategory = null,
    List<string>? Tags = null
);

public record UpdateFormulaDto(
    string? Name = null,
    string? Description = null,
    string? LatexContent = null,
    string? Category = null,
    string? Subcategory = null,
    List<string>? Tags = null
);

public record FormulaSearchDto(
    string? Query = null,
    string? Category = null,
    string? Subcategory = null,
    bool? FavoritesOnly = null,
    bool IncludeSystem = true,
    int Page = 1,
    int PageSize = 50
);

public record FormulaPageDto(
    List<FormulaDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);
