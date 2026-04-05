namespace Lilia.Core.DTOs;

public record HelpArticleListDto(
    Guid Id,
    string Title,
    string? HelpCategory,
    int HelpOrder,
    string? HelpSlug
);

public record HelpArticleDetailDto(
    Guid Id,
    string Title,
    string? HelpCategory,
    int HelpOrder,
    string? HelpSlug,
    List<BlockDto> Blocks
);

public record HelpCategoryDto(
    string Category,
    string Label,
    int ArticleCount
);
