namespace Lilia.Core.DTOs;

public record TeamDto(
    Guid Id,
    string Name,
    string? Slug,
    string? Image,
    string OwnerId,
    string? OwnerName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MemberCount
);

public record TeamDetailsDto(
    Guid Id,
    string Name,
    string? Slug,
    string? Image,
    string OwnerId,
    string? OwnerName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<TeamMemberDto> Members
);

public record TeamMemberDto(
    string UserId,
    string? Name,
    string? Email,
    string? Image,
    string RoleName,
    DateTime JoinedAt
);

public record CreateTeamDto(
    string Name,
    string? Slug,
    string? Image
);

public record UpdateTeamDto(
    string? Name,
    string? Slug,
    string? Image
);

public record InviteTeamMemberDto(
    string Email,
    string Role
);

public record UpdateTeamMemberRoleDto(
    string Role
);
