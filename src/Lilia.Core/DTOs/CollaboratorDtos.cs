namespace Lilia.Core.DTOs;

public record CollaboratorListDto(
    List<UserCollaboratorDto> Users,
    List<GroupCollaboratorDto> Groups
);

public record UserCollaboratorDto(
    Guid Id,
    string UserId,
    string? Name,
    string? Email,
    string? Image,
    string RoleName,
    Guid RoleId,
    DateTime CreatedAt
);

public record GroupCollaboratorDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string RoleName,
    Guid RoleId,
    int MemberCount,
    DateTime CreatedAt
);

public record AddUserCollaboratorDto(
    string UserId,
    string Role
);

public record AddGroupCollaboratorDto(
    Guid GroupId,
    string Role
);

public record UpdateCollaboratorRoleDto(
    string Role
);
