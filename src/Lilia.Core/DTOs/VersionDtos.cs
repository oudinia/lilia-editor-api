using System.Text.Json;

namespace Lilia.Core.DTOs;

public record VersionListDto(
    Guid Id,
    int VersionNumber,
    string? Name,
    bool IsAutoSave,
    string? CreatedBy,
    string? CreatorName,
    DateTime CreatedAt,
    /// <summary>
    /// The version the document currently holds — the marker moves back when you
    /// restore, rather than a new row being appended.
    ///
    /// Validated on read rather than trusted: the stored pointer is compared
    /// against the document's actual content, so an edit through any of the
    /// couple of dozen paths that touch blocks silently makes this false again
    /// without any of them having to remember to clear it.
    /// </summary>
    bool IsCurrent = false
);

public record VersionDto(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string? Name,
    bool IsAutoSave,
    JsonElement Snapshot,
    string? CreatedBy,
    string? CreatorName,
    DateTime CreatedAt
);

public record CreateVersionDto(
    string? Name
);

public record BranchVersionDto(
    /// <summary>
    /// Title for the new document. Optional — omitted, it is derived from the
    /// snapshot's own title and the version it came from, so the branch says
    /// where it came from without the caller having to invent a name.
    /// </summary>
    string? Title = null
);
