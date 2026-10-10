using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ICollaboratorService
{
    Task<CollaboratorListDto> GetCollaboratorsAsync(Guid documentId);
    Task<UserCollaboratorDto?> AddUserCollaboratorAsync(Guid documentId, string userId, AddUserCollaboratorDto dto);
    Task<GroupCollaboratorDto?> AddGroupCollaboratorAsync(Guid documentId, string userId, AddGroupCollaboratorDto dto);
    Task<UserCollaboratorDto?> UpdateUserCollaboratorRoleAsync(Guid documentId, string targetUserId, string userId, UpdateCollaboratorRoleDto dto);
    Task<GroupCollaboratorDto?> UpdateGroupCollaboratorRoleAsync(Guid documentId, Guid groupId, string userId, UpdateCollaboratorRoleDto dto);
    /// <summary>Removes access. Tells the person only when <paramref name="notify"/>; replies reach the owner only when <paramref name="allowReply"/>.</summary>
    Task<bool> RemoveUserCollaboratorAsync(Guid documentId, string targetUserId, string userId, bool notify = false, bool allowReply = false);
    Task<bool> RemoveGroupCollaboratorAsync(Guid documentId, Guid groupId, string userId);
    Task<InviteResultDto> InviteByEmailAsync(Guid documentId, string inviterUserId, InviteCollaboratorDto dto);
}
