namespace Lilia.Core.DTOs;

/// <summary>What the signed-in user did recently, grouped by tool (GET /api/recent).</summary>
/// <remarks>Tools with nothing to show are left out; the rest run most recent first.</remarks>
public record RecentActivityDto(IReadOnlyList<RecentToolDto> Tools);

/// <summary>One tool's recent items. <c>LastUsedAt</c> is the newest item's <c>At</c>.</summary>
public record RecentToolDto(string Tool, DateTime LastUsedAt, IReadOnlyList<RecentItemDto> Items);

/// <summary>
/// One recent thing. <c>At</c> is what the item is ordered by; the rest is
/// present only where the tool has it (nulls are omitted on the wire).
/// </summary>
/// <param name="Action">
/// An audited act of the user's own (<c>document.create</c>, <c>document.import</c>,
/// <c>document.export</c>, <c>document.restore</c>, <c>document.public.copy</c>,
/// <c>template.create</c>) — a recorded fact, never inferred.
/// </param>
/// <param name="ActionFormat">The import/export format, lower-cased.</param>
public record RecentItemDto(
    Guid Id,
    string Title,
    DateTime At,
    DateTime? UpdatedAt = null,
    string? Action = null,
    DateTime? ActionAt = null,
    string? ActionFormat = null,
    Guid? DocumentId = null,
    int? DocumentCount = null);
