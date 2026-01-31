namespace Lilia.Core.DTOs;

public record LabelDto(
    Guid Id,
    string Name,
    string? Color,
    DateTime CreatedAt
);

public record CreateLabelDto(
    string Name,
    string? Color
);

public record UpdateLabelDto(
    string? Name,
    string? Color
);
