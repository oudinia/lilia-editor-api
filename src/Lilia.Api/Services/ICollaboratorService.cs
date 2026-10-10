using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ICollaboratorService
{
    Task<CollaboratorListDto> GetCollaboratorsAsync(Guid documentId);
    /// <summary>Adds (or updates) a collaborator. <paramref name="silent"/>: no notification and no email (an undone removal).</summary>
    Task<UserCollaboratorDto?> AddUserCollaboratorAsync(Guid documentId, string userId, AddUserCollaboratorDto dto, bool silent = false);

    /// <summary>Tells someone already removed that they were, after the Undo window (Olivia, 10 Oct). False when they have access again.</summary>
    Task<bool> SendRemovalNoticeAsync(Guid documentId, string targetUserId, string userId, bool allowReply);
    Task<GroupCollaboratorDto?> AddGroupCollaboratorAsync(Guid documentId, string userId, AddGroupCollaboratorDto dto);
    Task<UserCollaboratorDto?> UpdateUserCollaboratorRoleAsync(Guid documentId, string targetUserId, string userId, UpdateCollaboratorRoleDto dto);
    Task<GroupCollaboratorDto?> UpdateGroupCollaboratorRoleAsync(Guid documentId, Guid groupId, string userId, UpdateCollaboratorRoleDto dto);
    /// <summary>Removes access. Tells the person only when <paramref name="notify"/>; replies reach the owner only when <paramref name="allowReply"/>.</summary>
    Task<bool> RemoveUserCollaboratorAsync(Guid documentId, string targetUserId, string userId, bool notify = false, bool allowReply = false);
    Task<bool> RemoveGroupCollaboratorAsync(Guid documentId, Guid groupId, string userId);
    Task<InviteResultDto> InviteByEmailAsync(Guid documentId, string inviterUserId, InviteCollaboratorDto dto);
}
