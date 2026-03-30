using System.Text.Json;

namespace Lilia.Core.DTOs;

public record DraftBlockDto(
    Guid Id,
    string UserId,
    string? Name,
    string Type,
    JsonElement Content,
    JsonElement Metadata,
    string? Category,
    List<string> Tags,
    bool IsFavorite,
    int UsageCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateDraftBlockDto(
    string Type,
    JsonElement? Content = null,
    string? Name = null,
    string? Category = null,
    List<string>? Tags = null,
    JsonElement? Metadata = null
);

public record UpdateDraftBlockDto(
    string? Name = null,
    string? Type = null,
    JsonElement? Content = null,
    JsonElement? Metadata = null,
    string? Category = null,
    List<string>? Tags = null
);

public record CommitDraftBlockDto(
    Guid DocumentId,
    int? SortOrder = null,
    Guid? ParentId = null,
    int? Depth = null
);

public record CreateDraftFromBlockDto(
    Guid DocumentId,
    Guid BlockId
);
