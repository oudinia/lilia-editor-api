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

public record InviteCollaboratorDto(
    string Email,
    string Role
);

/// <summary>
/// An invitation not yet accepted, for the share drawer (Olivia, 10 Oct, §c):
/// <c>pending</c>, or <c>expired</c> once <see cref="ExpiresAt"/> has passed.
/// <see cref="SentAt"/> is the last send; a resend moves it and the expiry.
/// </summary>
public record PendingInviteDto(
    Guid Id,
    string Email,
    string Role,
    string Status,
    DateTime InvitedAt,
    DateTime SentAt,
    DateTime ExpiresAt
);

public record InviteResultDto(
    bool Success,
    bool UserFound,
    string Email,
    string? Message
);

// Spec iter 7 — landing page resolver. Anyone with the token can
// see who/what/why (no PII other than what the invite already
// disclosed in the email).
public record InviteResolveDto(
    Guid Token,
    string Email,
    Guid DocumentId,
    string DocumentTitle,
    string InviterName,
    string Role,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt
);

public record InviteAcceptResultDto(
    Guid DocumentId
);
