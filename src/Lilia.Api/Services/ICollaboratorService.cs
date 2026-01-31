using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ICollaboratorService
{
    Task<CollaboratorListDto> GetCollaboratorsAsync(Guid documentId);
    Task<UserCollaboratorDto?> AddUserCollaboratorAsync(Guid documentId, string userId, AddUserCollaboratorDto dto);
    Task<GroupCollaboratorDto?> AddGroupCollaboratorAsync(Guid documentId, string userId, AddGroupCollaboratorDto dto);
    Task<UserCollaboratorDto?> UpdateUserCollaboratorRoleAsync(Guid documentId, string targetUserId, string userId, UpdateCollaboratorRoleDto dto);
    Task<GroupCollaboratorDto?> UpdateGroupCollaboratorRoleAsync(Guid documentId, Guid groupId, string userId, UpdateCollaboratorRoleDto dto);
    Task<bool> RemoveUserCollaboratorAsync(Guid documentId, string targetUserId, string userId);
    Task<bool> RemoveGroupCollaboratorAsync(Guid documentId, Guid groupId, string userId);
}
