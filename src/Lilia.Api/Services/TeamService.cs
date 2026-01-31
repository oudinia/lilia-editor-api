using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class TeamService : ITeamService
{
    private readonly LiliaDbContext _context;
    private readonly IUserService _userService;

    public TeamService(LiliaDbContext context, IUserService userService)
    {
        _context = context;
        _userService = userService;
    }

    public async Task<List<TeamDto>> GetTeamsAsync(string userId)
    {
        var teams = await _context.Teams
            .Include(t => t.Owner)
            .Include(t => t.Groups)
                .ThenInclude(g => g.Members)
            .Where(t => t.OwnerId == userId || t.Groups.Any(g => g.Members.Any(m => m.UserId == userId)))
            .ToListAsync();

        return teams.Select(t => new TeamDto(
            t.Id,
            t.Name,
            t.Slug,
            t.Image,
            t.OwnerId,
            t.Owner?.Name,
            t.CreatedAt,
            t.UpdatedAt,
            t.Groups.SelectMany(g => g.Members).Select(m => m.UserId).Distinct().Count()
        )).ToList();
    }

    public async Task<TeamDetailsDto?> GetTeamAsync(Guid teamId, string userId)
    {
        var team = await _context.Teams
            .Include(t => t.Owner)
            .Include(t => t.Groups)
                .ThenInclude(g => g.Members)
                    .ThenInclude(m => m.User)
            .Include(t => t.Groups)
                .ThenInclude(g => g.Members)
                    .ThenInclude(m => m.Role)
            .FirstOrDefaultAsync(t => t.Id == teamId);

        if (team == null) return null;

        // Check access
        if (team.OwnerId != userId && !team.Groups.Any(g => g.Members.Any(m => m.UserId == userId)))
            return null;

        var members = team.Groups
            .SelectMany(g => g.Members)
            .GroupBy(m => m.UserId)
            .Select(g => g.First())
            .Select(m => new TeamMemberDto(
                m.UserId,
                m.User?.Name,
                m.User?.Email,
                m.User?.Image,
                m.Role.Name,
                m.CreatedAt
            ))
            .ToList();

        return new TeamDetailsDto(
            team.Id,
            team.Name,
            team.Slug,
            team.Image,
            team.OwnerId,
            team.Owner?.Name,
            team.CreatedAt,
            team.UpdatedAt,
            members
        );
    }

    public async Task<TeamDto> CreateTeamAsync(string userId, CreateTeamDto dto)
    {
        var ownerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Owner);
        if (ownerRole == null) throw new InvalidOperationException("Owner role not found");

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Slug = dto.Slug ?? GenerateSlug(dto.Name),
            Image = dto.Image,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Create default group
        var defaultGroup = new Group
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Name = "Everyone",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        // Add owner as member
        var membership = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = defaultGroup.Id,
            UserId = userId,
            RoleId = ownerRole.Id,
            CreatedAt = DateTime.UtcNow
        };

        team.Groups.Add(defaultGroup);
        defaultGroup.Members.Add(membership);

        _context.Teams.Add(team);
        await _context.SaveChangesAsync();

        var owner = await _context.Users.FindAsync(userId);

        return new TeamDto(
            team.Id,
            team.Name,
            team.Slug,
            team.Image,
            team.OwnerId,
            owner?.Name,
            team.CreatedAt,
            team.UpdatedAt,
            1
        );
    }

    public async Task<TeamDto?> UpdateTeamAsync(Guid teamId, string userId, UpdateTeamDto dto)
    {
        var team = await _context.Teams
            .Include(t => t.Owner)
            .FirstOrDefaultAsync(t => t.Id == teamId);

        if (team == null || team.OwnerId != userId) return null;

        if (dto.Name != null) team.Name = dto.Name;
        if (dto.Slug != null) team.Slug = dto.Slug;
        if (dto.Image != null) team.Image = dto.Image;

        team.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var memberCount = await _context.GroupMembers
            .Where(gm => gm.Group.TeamId == teamId)
            .Select(gm => gm.UserId)
            .Distinct()
            .CountAsync();

        return new TeamDto(
            team.Id,
            team.Name,
            team.Slug,
            team.Image,
            team.OwnerId,
            team.Owner?.Name,
            team.CreatedAt,
            team.UpdatedAt,
            memberCount
        );
    }

    public async Task<bool> DeleteTeamAsync(Guid teamId, string userId)
    {
        var team = await _context.Teams.FindAsync(teamId);
        if (team == null || team.OwnerId != userId) return false;

        _context.Teams.Remove(team);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<TeamMemberDto>> GetTeamMembersAsync(Guid teamId, string userId)
    {
        var team = await _context.Teams
            .Include(t => t.Groups)
                .ThenInclude(g => g.Members)
                    .ThenInclude(m => m.User)
            .Include(t => t.Groups)
                .ThenInclude(g => g.Members)
                    .ThenInclude(m => m.Role)
            .FirstOrDefaultAsync(t => t.Id == teamId);

        if (team == null) return new List<TeamMemberDto>();

        // Check access
        if (team.OwnerId != userId && !team.Groups.Any(g => g.Members.Any(m => m.UserId == userId)))
            return new List<TeamMemberDto>();

        return team.Groups
            .SelectMany(g => g.Members)
            .GroupBy(m => m.UserId)
            .Select(g => g.First())
            .Select(m => new TeamMemberDto(
                m.UserId,
                m.User?.Name,
                m.User?.Email,
                m.User?.Image,
                m.Role.Name,
                m.CreatedAt
            ))
            .ToList();
    }

    public async Task<TeamMemberDto?> InviteMemberAsync(Guid teamId, string userId, InviteTeamMemberDto dto)
    {
        var team = await _context.Teams
            .Include(t => t.Groups.Where(g => g.IsDefault))
            .FirstOrDefaultAsync(t => t.Id == teamId);

        if (team == null || team.OwnerId != userId) return null;

        var targetUser = await _userService.GetUserByEmailAsync(dto.Email);
        if (targetUser == null) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
        if (role == null) return null;

        var defaultGroup = team.Groups.FirstOrDefault(g => g.IsDefault);
        if (defaultGroup == null) return null;

        // Check if already a member
        var existingMember = await _context.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == defaultGroup.Id && gm.UserId == targetUser.Id);

        if (existingMember != null)
        {
            existingMember.RoleId = role.Id;
        }
        else
        {
            var membership = new GroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = defaultGroup.Id,
                UserId = targetUser.Id,
                RoleId = role.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.GroupMembers.Add(membership);
        }

        await _context.SaveChangesAsync();

        return new TeamMemberDto(
            targetUser.Id,
            targetUser.Name,
            targetUser.Email,
            targetUser.Image,
            role.Name,
            DateTime.UtcNow
        );
    }

    public async Task<TeamMemberDto?> UpdateMemberRoleAsync(Guid teamId, string targetUserId, string userId, UpdateTeamMemberRoleDto dto)
    {
        var team = await _context.Teams.FindAsync(teamId);
        if (team == null || team.OwnerId != userId) return null;

        var membership = await _context.GroupMembers
            .Include(gm => gm.User)
            .Include(gm => gm.Role)
            .FirstOrDefaultAsync(gm => gm.Group.TeamId == teamId && gm.UserId == targetUserId);

        if (membership == null) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
        if (role == null) return null;

        membership.RoleId = role.Id;
        await _context.SaveChangesAsync();

        return new TeamMemberDto(
            membership.UserId,
            membership.User?.Name,
            membership.User?.Email,
            membership.User?.Image,
            role.Name,
            membership.CreatedAt
        );
    }

    public async Task<bool> RemoveMemberAsync(Guid teamId, string targetUserId, string userId)
    {
        var team = await _context.Teams.FindAsync(teamId);
        if (team == null || team.OwnerId != userId) return false;

        // Cannot remove the owner
        if (targetUserId == team.OwnerId) return false;

        var memberships = await _context.GroupMembers
            .Where(gm => gm.Group.TeamId == teamId && gm.UserId == targetUserId)
            .ToListAsync();

        if (!memberships.Any()) return false;

        _context.GroupMembers.RemoveRange(memberships);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<DocumentListDto>> GetTeamDocumentsAsync(Guid teamId, string userId)
    {
        var team = await _context.Teams
            .Include(t => t.Groups)
                .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(t => t.Id == teamId);

        if (team == null) return new List<DocumentListDto>();

        // Check access
        if (team.OwnerId != userId && !team.Groups.Any(g => g.Members.Any(m => m.UserId == userId)))
            return new List<DocumentListDto>();

        var documents = await _context.Documents
            .Include(d => d.Owner)
            .Include(d => d.DocumentLabels)
                .ThenInclude(dl => dl.Label)
            .Where(d => d.TeamId == teamId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();

        return documents.Select(d => new DocumentListDto(
            d.Id,
            d.Title,
            d.OwnerId,
            d.Owner?.Name,
            d.TeamId,
            team.Name,
            d.CreatedAt,
            d.UpdatedAt,
            d.LastOpenedAt,
            d.DocumentLabels.Select(dl => new LabelDto(
                dl.Label.Id,
                dl.Label.Name,
                dl.Label.Color,
                dl.Label.CreatedAt
            )).ToList()
        )).ToList();
    }

    private static string GenerateSlug(string name)
    {
        return name.ToLower()
            .Replace(" ", "-")
            .Replace("_", "-")
            + "-" + Guid.NewGuid().ToString("N")[..6];
    }
}
