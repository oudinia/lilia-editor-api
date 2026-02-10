using System.Text.Json;

namespace Lilia.Core.DTOs;

public record BlockDto(
    Guid Id,
    Guid DocumentId,
    string Type,
    JsonElement Content,
    int SortOrder,
    Guid? ParentId,
    int Depth,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateBlockDto(
    string Type,
    JsonElement? Content,
    int? SortOrder,
    Guid? ParentId,
    int? Depth
);

public record UpdateBlockDto(
    string? Type,
    JsonElement? Content,
    int? SortOrder,
    Guid? ParentId,
    int? Depth
);

public record BatchUpdateBlockDto(
    Guid Id,
    string? Type,
    JsonElement? Content,
    int? SortOrder,
    Guid? ParentId,
    int? Depth
);

public record BatchUpdateBlocksDto(
    List<BatchUpdateBlockDto> Blocks
);

public record ReorderBlocksDto(
    List<Guid> BlockIds
);

public record ConvertBlockDto(
    string NewType
);

public record BlockTypeMetadataDto(
    string Type,
    string Label,
    string Description,
    string IconName,
    JsonElement DefaultContent
);
