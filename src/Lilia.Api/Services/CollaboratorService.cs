using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

public class SharedMessages { }

public class CollaboratorService : ICollaboratorService
{
    private readonly LiliaDbContext _context;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly INotificationService _notificationService;
    private readonly IStringLocalizer<SharedMessages> _localizer;

    public CollaboratorService(LiliaDbContext context, IEmailService emailService, EmailSettings emailSettings, INotificationService notificationService, IStringLocalizer<SharedMessages> localizer)
    {
        _context = context;
        _emailService = emailService;
        _emailSettings = emailSettings;
        _notificationService = notificationService;
        _localizer = localizer;
    }

    public async Task<CollaboratorListDto> GetCollaboratorsAsync(Guid documentId)
    {
        var userCollaborators = await _context.DocumentCollaborators
            .Include(dc => dc.User)
            .Include(dc => dc.Role)
            .Where(dc => dc.DocumentId == documentId)
            .Select(dc => new UserCollaboratorDto(
                dc.Id,
                dc.UserId,
                dc.User.Name,
                dc.User.Email,
                dc.User.Image,
                dc.Role.Name,
                dc.RoleId,
                dc.CreatedAt
            ))
            .ToListAsync();

        var groupCollaborators = await _context.DocumentGroups
            .Include(dg => dg.Group)
                .ThenInclude(g => g.Members)
            .Include(dg => dg.Role)
            .Where(dg => dg.DocumentId == documentId)
            .Select(dg => new GroupCollaboratorDto(
                dg.Id,
                dg.GroupId,
                dg.Group.Name,
                dg.Role.Name,
                dg.RoleId,
                dg.Group.Members.Count,
                dg.CreatedAt
            ))
            .ToListAsync();

        return new CollaboratorListDto(userCollaborators, groupCollaborators);
    }

    public async Task<UserCollaboratorDto?> AddUserCollaboratorAsync(Guid documentId, string userId, AddUserCollaboratorDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null) return null;

        // Only owner can manage collaborators
        if (document.OwnerId != userId) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
        if (role == null) return null;

        var targetUser = await _context.Users.FindAsync(dto.UserId);
        if (targetUser == null) return null;

        // Check if already a collaborator
        var existing = await _context.DocumentCollaborators
            .FirstOrDefaultAsync(dc => dc.DocumentId == documentId && dc.UserId == dto.UserId);

        if (existing != null)
        {
            existing.RoleId = role.Id;
            await _context.SaveChangesAsync();

            return new UserCollaboratorDto(
                existing.Id,
                existing.UserId,
                targetUser.Name,
                targetUser.Email,
                targetUser.Image,
                role.Name,
                role.Id,
                existing.CreatedAt
            );
        }

        var collaborator = new DocumentCollaborator
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            UserId = dto.UserId,
            RoleId = role.Id,
            InvitedBy = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentCollaborators.Add(collaborator);
        await _context.SaveChangesAsync();

        // Notify the added collaborator
        try
        {
            var inviterUser = await _context.Users.FindAsync(userId);
            var inviterName = inviterUser?.Name ?? inviterUser?.Email ?? "Someone";
            await _notificationService.CreateAsync(
                dto.UserId,
                "document_shared",
                string.Format(_localizer["NotificationShared"].Value, inviterName, document.Title),
                string.Format(_localizer["NotificationAccess"].Value, role.Name),
                $"/document/{documentId}"
            );
        }
        catch
        {
            // Notification failure should not block the operation
        }

        return new UserCollaboratorDto(
            collaborator.Id,
            collaborator.UserId,
            targetUser.Name,
            targetUser.Email,
            targetUser.Image,
            role.Name,
            role.Id,
            collaborator.CreatedAt
        );
    }

    public async Task<GroupCollaboratorDto?> AddGroupCollaboratorAsync(Guid documentId, string userId, AddGroupCollaboratorDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null) return null;

        if (document.OwnerId != userId) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
        if (role == null) return null;

        var group = await _context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);
        if (group == null) return null;

        // Check if already shared with group
        var existing = await _context.DocumentGroups
            .FirstOrDefaultAsync(dg => dg.DocumentId == documentId && dg.GroupId == dto.GroupId);

        if (existing != null)
        {
            existing.RoleId = role.Id;
            await _context.SaveChangesAsync();

            return new GroupCollaboratorDto(
                existing.Id,
                existing.GroupId,
                group.Name,
                role.Name,
                role.Id,
                group.Members.Count,
                existing.CreatedAt
            );
        }

        var documentGroup = new DocumentGroup
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            GroupId = dto.GroupId,
            RoleId = role.Id,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentGroups.Add(documentGroup);
        await _context.SaveChangesAsync();

        return new GroupCollaboratorDto(
            documentGroup.Id,
            documentGroup.GroupId,
            group.Name,
            role.Name,
            role.Id,
            group.Members.Count,
            documentGroup.CreatedAt
        );
    }

    public async Task<UserCollaboratorDto?> UpdateUserCollaboratorRoleAsync(Guid documentId, string targetUserId, string userId, UpdateCollaboratorRoleDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return null;

        var collaborator = await _context.DocumentCollaborators
            .Include(dc => dc.User)
            .FirstOrDefaultAsync(dc => dc.DocumentId == documentId && dc.UserId == targetUserId);

        if (collaborator == null) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
        if (role == null) return null;

        collaborator.RoleId = role.Id;
        await _context.SaveChangesAsync();

        return new UserCollaboratorDto(
            collaborator.Id,
            collaborator.UserId,
            collaborator.User?.Name,
            collaborator.User?.Email,
            collaborator.User?.Image,
            role.Name,
            role.Id,
            collaborator.CreatedAt
        );
    }

    public async Task<GroupCollaboratorDto?> UpdateGroupCollaboratorRoleAsync(Guid documentId, Guid groupId, string userId, UpdateCollaboratorRoleDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return null;

        var documentGroup = await _context.DocumentGroups
            .Include(dg => dg.Group)
                .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(dg => dg.DocumentId == documentId && dg.GroupId == groupId);

        if (documentGroup == null) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
        if (role == null) return null;

        documentGroup.RoleId = role.Id;
        await _context.SaveChangesAsync();

        return new GroupCollaboratorDto(
            documentGroup.Id,
            documentGroup.GroupId,
            documentGroup.Group.Name,
            role.Name,
            role.Id,
            documentGroup.Group.Members.Count,
            documentGroup.CreatedAt
        );
    }

    public async Task<bool> RemoveUserCollaboratorAsync(Guid documentId, string targetUserId, string userId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return false;

        var collaborator = await _context.DocumentCollaborators
            .FirstOrDefaultAsync(dc => dc.DocumentId == documentId && dc.UserId == targetUserId);

        if (collaborator == null) return false;

        _context.DocumentCollaborators.Remove(collaborator);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RemoveGroupCollaboratorAsync(Guid documentId, Guid groupId, string userId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return false;

        var documentGroup = await _context.DocumentGroups
            .FirstOrDefaultAsync(dg => dg.DocumentId == documentId && dg.GroupId == groupId);

        if (documentGroup == null) return false;

        _context.DocumentGroups.Remove(documentGroup);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<InviteResultDto> InviteByEmailAsync(Guid documentId, string inviterUserId, InviteCollaboratorDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null)
            return new InviteResultDto(false, false, dto.Email, _localizer["DocumentNotFound"].Value);

        if (document.OwnerId != inviterUserId)
            return new InviteResultDto(false, false, dto.Email, _localizer["OnlyOwnerCanInvite"].Value);

        var inviter = await _context.Users.FindAsync(inviterUserId);
        var inviterName = inviter?.Name ?? inviter?.Email ?? "Someone";
        var documentUrl = $"{_emailSettings.BaseUrl}/documents/{documentId}";

        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (targetUser == null)
        {
            // User not registered — store pending invite
            var existingInvite = await _context.DocumentPendingInvites
                .FirstOrDefaultAsync(pi => pi.DocumentId == documentId && pi.Email == dto.Email && pi.Status == "pending");

            if (existingInvite != null)
            {
                existingInvite.Role = dto.Role;
                existingInvite.ExpiresAt = DateTime.UtcNow.AddDays(30);
                await _context.SaveChangesAsync();
            }
            else
            {
                var pendingInvite = new DocumentPendingInvite
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Email = dto.Email,
                    Role = dto.Role,
                    InvitedBy = inviterUserId,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(30)
                };
                _context.DocumentPendingInvites.Add(pendingInvite);
                await _context.SaveChangesAsync();
            }

            // Send invite email with sign-up link
            var signUpUrl = $"{_emailSettings.BaseUrl}/sign-up?email={Uri.EscapeDataString(dto.Email)}";
            try
            {
                await _emailService.SendDocumentInviteAsync(dto.Email, inviterName, document.Title, dto.Role, signUpUrl);
            }
            catch
            {
                // Email failure should not block the invite
            }

            return new InviteResultDto(true, false, dto.Email, _localizer["InvitationSent"].Value);
        }

        // Prevent inviting yourself
        if (targetUser.Id == inviterUserId)
            return new InviteResultDto(false, true, dto.Email, _localizer["CannotInviteSelf"].Value);

        var result = await AddUserCollaboratorAsync(documentId, inviterUserId, new AddUserCollaboratorDto(targetUser.Id, dto.Role));
        if (result == null)
            return new InviteResultDto(false, true, dto.Email, _localizer["FailedToAddCollaborator"].Value);

        // Send invite email
        try
        {
            await _emailService.SendDocumentInviteAsync(dto.Email, inviterName, document.Title, dto.Role, documentUrl);
        }
        catch
        {
            // Email failure should not block the invite — collaborator was already added
        }

        return new InviteResultDto(true, true, dto.Email, null);
    }
}
