using System.Text.Json;

namespace Lilia.Core.DTOs;

public record VersionListDto(
    Guid Id,
    int VersionNumber,
    string? Name,
    string? CreatedBy,
    string? CreatorName,
    DateTime CreatedAt
);

public record VersionDto(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string? Name,
    JsonElement Snapshot,
    string? CreatedBy,
    string? CreatorName,
    DateTime CreatedAt
);

public record CreateVersionDto(
    string? Name
);
