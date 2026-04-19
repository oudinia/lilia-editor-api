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

/// <summary>Direct add-by-userId — no email, uses the user search path.</summary>
public record AddTeamMemberByUserIdDto(
    string UserId,
    string Role
);

public record UpdateTeamMemberRoleDto(
    string Role
);
