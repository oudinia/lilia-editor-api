using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ITeamService
{
    Task<List<TeamDto>> GetTeamsAsync(string userId);
    Task<TeamDetailsDto?> GetTeamAsync(Guid teamId, string userId);
    Task<TeamDto> CreateTeamAsync(string userId, CreateTeamDto dto);
    Task<TeamDto?> UpdateTeamAsync(Guid teamId, string userId, UpdateTeamDto dto);
    Task<bool> DeleteTeamAsync(Guid teamId, string userId);
    Task<List<TeamMemberDto>> GetTeamMembersAsync(Guid teamId, string userId);
    Task<TeamMemberDto?> InviteMemberAsync(Guid teamId, string userId, InviteTeamMemberDto dto);
    Task<TeamMemberDto?> UpdateMemberRoleAsync(Guid teamId, string targetUserId, string userId, UpdateTeamMemberRoleDto dto);
    Task<bool> RemoveMemberAsync(Guid teamId, string targetUserId, string userId);
    Task<List<DocumentListDto>> GetTeamDocumentsAsync(Guid teamId, string userId);
}
