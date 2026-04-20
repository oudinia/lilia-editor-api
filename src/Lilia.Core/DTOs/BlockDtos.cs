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

/// <summary>
/// Tier 1 bulk-convert request. <see cref="BlockIds"/> must be non-empty and
/// belong to the target document. <see cref="Action"/> is one of:
/// "to_list", "to_ordered_list", "merge_paragraph", "reheading".
/// <see cref="HeadingLevel"/> is required for "reheading" (1-6).
/// </summary>
public record BatchConvertBlocksDto(
    List<Guid> BlockIds,
    string Action,
    int? HeadingLevel
);

public record BatchConvertResultDto(
    List<BlockDto> Created,
    List<Guid> DeletedIds
);

public record BlockTypeMetadataDto(
    string Type,
    string Label,
    string Description,
    string IconName,
    string Category,
    JsonElement DefaultContent
);
