using System.Text.Json;

namespace Lilia.Core.DTOs;

/// <summary>
/// Read shape for a block group (LILIA-136). `MemberBlockIds` is the
/// set of blocks currently in the group; clients use it together with
/// the document's blocks to render group-aware layouts.
/// </summary>
public record BlockGroupDto(
    Guid Id,
    Guid DocumentId,
    string Dimension,
    JsonElement Attributes,
    string? Name,
    IReadOnlyList<Guid> MemberBlockIds,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Create a new group with an initial member set. The service rejects
/// the call if any of the member blocks already belong to a group in
/// the same dimension (the one-group-per-dimension-per-block rule).
/// </summary>
public record CreateBlockGroupDto(
    string Dimension,
    JsonElement Attributes,
    string? Name,
    IReadOnlyList<Guid> MemberBlockIds
);

/// <summary>
/// Patch a group. Any field set to null is left alone. To replace the
/// member set, send the full new set of block ids in
/// <c>MemberBlockIds</c>; pass an empty list to clear it; pass null to
/// leave membership untouched.
/// </summary>
public record UpdateBlockGroupDto(
    JsonElement? Attributes,
    string? Name,
    IReadOnlyList<Guid>? MemberBlockIds
);
