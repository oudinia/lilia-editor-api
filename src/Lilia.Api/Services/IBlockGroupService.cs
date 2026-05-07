using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

/// <summary>
/// Block-group service (LILIA-136). Manages the M:N relationship between
/// blocks and named groups, scoped to a document and a single
/// "dimension" (layout, review, numbering, …). Enforces the
/// one-group-per-dimension-per-block constraint at the application level.
/// </summary>
public interface IBlockGroupService
{
    /// <summary>List all groups attached to a document, including their member block ids.</summary>
    Task<List<BlockGroupDto>> GetGroupsAsync(Guid documentId);

    /// <summary>Get a single group by id, scoped to its document.</summary>
    Task<BlockGroupDto?> GetGroupAsync(Guid documentId, Guid groupId);

    /// <summary>
    /// Create a new group with the given dimension/attributes and an
    /// initial set of member blocks. Returns null if the document
    /// doesn't exist; throws <see cref="InvalidOperationException"/> if
    /// any member block already belongs to a group in the same dimension.
    /// </summary>
    Task<BlockGroupDto?> CreateGroupAsync(Guid documentId, CreateBlockGroupDto dto);

    /// <summary>
    /// Patch attributes / name / membership. Membership replacement is
    /// destructive — the group's member set is replaced by the given list.
    /// Same conflict check as Create.
    /// </summary>
    Task<BlockGroupDto?> UpdateGroupAsync(Guid documentId, Guid groupId, UpdateBlockGroupDto dto);

    /// <summary>Delete a group and all its memberships (cascade).</summary>
    Task<bool> DeleteGroupAsync(Guid documentId, Guid groupId);
}
