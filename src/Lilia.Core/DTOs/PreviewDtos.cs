namespace Lilia.Core.DTOs;

public record PageCountResponse(int Count);

public record SectionDto(
    string Id,
    string Title,
    int Level,
    int StartPage,
    int EndPage
);

public record SectionsResponse(List<SectionDto> Sections);

public record PreviewResponse(
    string Content,
    string Format,
    int? Page,
    int? TotalPages,
    DateTime GeneratedAt,
    string? CacheKey
);
