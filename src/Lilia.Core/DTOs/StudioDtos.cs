using System.Text.Json;

namespace Lilia.Core.DTOs;

// Lightweight tree node — no full content, includes preview text for outline
public record StudioBlockNodeDto(
    Guid Id,
    string Type,
    string? Path,
    int SortOrder,
    Guid? ParentId,
    int Depth,
    string Status,
    JsonElement Metadata,
    string? Preview
);

// Full block with content — loaded when focused
public record StudioBlockDetailDto(
    Guid Id,
    string Type,
    JsonElement Content,
    string? Path,
    int SortOrder,
    Guid? ParentId,
    int Depth,
    string Status,
    JsonElement Metadata,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record StudioTreeDto(
    Guid DocumentId,
    string Title,
    List<StudioBlockNodeDto> Nodes
);

public record MoveBlockDto(
    Guid? NewParentId,
    int NewPosition
);

public record UpdateBlockMetadataDto(
    string? Status,
    JsonElement? Metadata
);

public record StudioSessionDto(
    Guid DocumentId,
    Guid? FocusedBlockId,
    JsonElement Layout,
    Guid[] CollapsedIds,
    Guid[] PinnedIds,
    string ViewMode,
    DateTime LastAccessed
);

public record SaveStudioSessionDto(
    Guid? FocusedBlockId,
    JsonElement? Layout,
    Guid[]? CollapsedIds,
    Guid[]? PinnedIds,
    string? ViewMode
);

public record BlockPreviewDto(
    Guid BlockId,
    string Format,
    byte[]? Data,
    DateTime RenderedAt
);

public record RenderBlockPreviewDto(
    string Format
);
