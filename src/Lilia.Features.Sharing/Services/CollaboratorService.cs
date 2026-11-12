using Lilia.Api.Directories;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Api.Features.Sharing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

public class SharedMessages { }

public class CollaboratorService : ICollaboratorService
{
    private readonly SharingDbContext _context;
    private readonly IDocumentDirectory _documents;
    private readonly IPeopleDirectory _people;
    private readonly ITeamDirectory _teams;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly INotificationService _notificationService;
    private readonly IStringLocalizer<SharedMessages> _localizer;
    private readonly ILogger<CollaboratorService> _logger;
    private readonly Wolverine.IMessageBus? _bus;

    private readonly ISharingNotifier? _sharingNotifier;

    /// <summary>An invite expires after 14 days (Olivia's spec, confirmed by the user, 10 Oct); it was 30.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    public CollaboratorService(SharingDbContext context, IDocumentDirectory documents, IPeopleDirectory people, ITeamDirectory teams, IEmailService emailService, EmailSettings emailSettings, INotificationService notificationService, IStringLocalizer<SharedMessages> localizer, ILogger<CollaboratorService> logger, Wolverine.IMessageBus? bus = null, ISharingNotifier? sharingNotifier = null)
    {
        _sharingNotifier = sharingNotifier;
        _context = context;
        _documents = documents;
        _people = people;
        _teams = teams;
        _emailService = emailService;
        _emailSettings = emailSettings;
        _notificationService = notificationService;
        _localizer = localizer;
        _logger = logger;
        _bus = bus;
    }

    public async Task<CollaboratorListDto> GetCollaboratorsAsync(Guid documentId)
    {
        var userRows = await _context.DocumentCollaborators
            .Where(dc => dc.DocumentId == documentId)
            .Select(dc => new { dc.Id, dc.UserId, Role = dc.Role.Name, dc.RoleId, dc.CreatedAt })
            .ToListAsync();
        // The owner comes first, marked "owner" (the bot's #83, 13 Nov 2026): the Share list showed only the
        // people it was shared with, while the design puts the owner inside it (with no role menu).
        var ownerId = (await _documents.FindAsync(documentId))?.OwnerId;
        // Names and pictures come from Accounts; a share whose account is gone isn't listed (as the join had it).
        var people = await _people.FindManyAsync(userRows.Select(r => r.UserId).Append(ownerId ?? "").Where(id => id != "").Distinct());
        var userCollaborators = userRows
            .Where(r => people.ContainsKey(r.UserId) && r.UserId != ownerId)
            .Select(r =>
            {
                var p = people[r.UserId];
                return new UserCollaboratorDto(r.Id, r.UserId, p.Name, p.Email, p.Image, r.Role, r.RoleId, r.CreatedAt);
            })
            .ToList();
        if (ownerId is not null && people.TryGetValue(ownerId, out var owner))
            userCollaborators.Insert(0, new UserCollaboratorDto(Guid.Empty, ownerId, owner.Name, owner.Email, owner.Image, RoleNames.Owner, Guid.Empty, DateTime.MinValue));

        var groupRows = await _context.DocumentGroups
            .Where(dg => dg.DocumentId == documentId)
            .Select(dg => new { dg.Id, dg.GroupId, Role = dg.Role.Name, dg.RoleId, dg.CreatedAt })
            .ToListAsync();
        var groups = await _teams.FindGroupsAsync(groupRows.Select(r => r.GroupId));
        var groupCollaborators = groupRows
            .Where(r => groups.ContainsKey(r.GroupId))
            .Select(r =>
            {
                var g = groups[r.GroupId];
                return new GroupCollaboratorDto(r.Id, r.GroupId, g.Name, r.Role, r.RoleId, g.MemberCount, r.CreatedAt);
            })
            .ToList();

        return new CollaboratorListDto(userCollaborators, groupCollaborators);
    }

    public async Task<UserCollaboratorDto?> AddUserCollaboratorAsync(Guid documentId, string userId, AddUserCollaboratorDto dto, bool silent = false)
    {
        var document = await _documents.FindAsync(documentId);
        if (document == null) return null;

        // Only owner can manage collaborators
        if (document.OwnerId != userId) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Normalize(dto.Role));
        if (role == null) return null;

        var targetUser = await _people.FindAsync(dto.UserId);
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

        // Undo of a removal puts someone back without a word (Olivia, 10 Oct).
        if (!silent)
        {
            // Notify the added collaborator (in-app)
            string? inviterName = null;
            try
            {
                var inviterUser = await _people.FindAsync(userId);
                inviterName = inviterUser?.Name ?? inviterUser?.Email ?? "Someone";
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

            // Publish for the email handler in Features/Sharing/Handlers.
            // Constructor-optional bus so legacy tests without DI still pass;
            // prod always supplies one. Inviter name re-resolves inline if
            // the in-app notification above failed to fetch it.
            if (_bus is not null)
            {
                if (inviterName is null)
                {
                    var inviter = await _people.FindAsync(userId);
                    inviterName = inviter?.Name ?? inviter?.Email ?? "Someone";
                }
                await _bus.PublishAsync(new Events.Common.DocumentSharedEvent(
                    documentId,
                    document.Title,
                    userId,
                    inviterName,
                    dto.UserId,
                    targetUser.Email,
                    role.Name));
            }
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
        var document = await _documents.FindAsync(documentId);
        if (document == null) return null;

        if (document.OwnerId != userId) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Normalize(dto.Role));
        if (role == null) return null;

        var group = await _teams.FindGroupAsync(dto.GroupId);
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
                group.MemberCount,
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
            group.MemberCount,
            documentGroup.CreatedAt
        );
    }

    public async Task<UserCollaboratorDto?> UpdateUserCollaboratorRoleAsync(Guid documentId, string targetUserId, string userId, UpdateCollaboratorRoleDto dto)
    {
        var document = await _documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return null;

        var collaborator = await _context.DocumentCollaborators
            .Include(dc => dc.Role)
            .FirstOrDefaultAsync(dc => dc.DocumentId == documentId && dc.UserId == targetUserId);

        if (collaborator == null) return null;
        var person = await _people.FindAsync(targetUserId);

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Normalize(dto.Role));
        if (role == null) return null;

        var oldRole = collaborator.Role?.Name ?? "";
        collaborator.RoleId = role.Id;
        await _context.SaveChangesAsync();
        if (_sharingNotifier is not null)
            await _sharingNotifier.RoleChangedAsync(documentId, targetUserId, userId, oldRole, role.Name);

        return new UserCollaboratorDto(
            collaborator.Id,
            collaborator.UserId,
            person?.Name,
            person?.Email,
            person?.Image,
            role.Name,
            role.Id,
            collaborator.CreatedAt
        );
    }

    public async Task<GroupCollaboratorDto?> UpdateGroupCollaboratorRoleAsync(Guid documentId, Guid groupId, string userId, UpdateCollaboratorRoleDto dto)
    {
        var document = await _documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return null;

        var documentGroup = await _context.DocumentGroups
            .FirstOrDefaultAsync(dg => dg.DocumentId == documentId && dg.GroupId == groupId);

        if (documentGroup == null) return null;
        // The join this replaces dropped a share whose group is gone.
        var group = await _teams.FindGroupAsync(groupId);
        if (group == null) return null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Normalize(dto.Role));
        if (role == null) return null;

        documentGroup.RoleId = role.Id;
        await _context.SaveChangesAsync();

        return new GroupCollaboratorDto(
            documentGroup.Id,
            documentGroup.GroupId,
            group.Name,
            role.Name,
            role.Id,
            group.MemberCount,
            documentGroup.CreatedAt
        );
    }

    public async Task<bool> SendRemovalNoticeAsync(Guid documentId, string targetUserId, string userId, bool allowReply)
    {
        var document = await _documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return false;
        // Undone in the meantime: they have access, so there is nothing to tell.
        if (await _context.DocumentCollaborators.AnyAsync(dc => dc.DocumentId == documentId && dc.UserId == targetUserId)) return false;
        if (_sharingNotifier is null) return false;
        await _sharingNotifier.AccessRemovedAsync(documentId, targetUserId, userId, allowReply);
        return true;
    }

    public async Task<bool> RemoveUserCollaboratorAsync(Guid documentId, string targetUserId, string userId, bool notify = false, bool allowReply = false)
    {
        var document = await _documents.FindAsync(documentId);
        if (document == null || document.OwnerId != userId) return false;

        var collaborator = await _context.DocumentCollaborators
            .FirstOrDefaultAsync(dc => dc.DocumentId == documentId && dc.UserId == targetUserId);

        if (collaborator == null) return false;

        _context.DocumentCollaborators.Remove(collaborator);
        await _context.SaveChangesAsync();
        // Silent unless the owner asks (the user, 10 Oct: removing someone
        // unwanted or harmful from a lab shouldn't tell them by default).
        if (notify && _sharingNotifier is not null)
            await _sharingNotifier.AccessRemovedAsync(documentId, targetUserId, userId, allowReply);

        return true;
    }

    public async Task<bool> RemoveGroupCollaboratorAsync(Guid documentId, Guid groupId, string userId)
    {
        var document = await _documents.FindAsync(documentId);
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
        var document = await _documents.FindAsync(documentId);
        if (document == null)
            return new InviteResultDto(false, false, dto.Email, _localizer["DocumentNotFound"].Value);

        if (document.OwnerId != inviterUserId)
            return new InviteResultDto(false, false, dto.Email, _localizer["OnlyOwnerCanInvite"].Value);

        var inviter = await _people.FindAsync(inviterUserId);
        var inviterName = inviter?.Name ?? inviter?.Email ?? "Someone";
        var documentUrl = $"{_emailSettings.BaseUrl}/documents/{documentId}";

        var targetUser = await _people.FindByEmailAsync(dto.Email);

        // Prevent inviting yourself — guard runs before pending row
        // creation so we don't accumulate noise.
        if (targetUser != null && targetUser.Id == inviterUserId)
            return new InviteResultDto(false, true, dto.Email, _localizer["CannotInviteSelf"].Value);

        // Spec iter 7 — always create (or refresh) a pending invite
        // row, even for already-registered users. The row's Id is
        // the token the email CTA links to (/invites/{token}), so
        // both flows (sign-up + already-signed-in) land on the same
        // landing page. The pending row stays in "pending" status
        // and is materialized to "accepted" by /invites/{token}/accept.
        var canonicalRole = RoleNames.Normalize(dto.Role);
        var existingInvite = await _context.DocumentPendingInvites
            .FirstOrDefaultAsync(pi => pi.DocumentId == documentId && pi.Email == dto.Email && pi.Status == "pending");
        Guid token;
        if (existingInvite != null)
        {
            existingInvite.Role = canonicalRole;
            existingInvite.ExpiresAt = DateTime.UtcNow.Add(InviteLifetime);
            token = existingInvite.Id;
        }
        else
        {
            var pendingInvite = new DocumentPendingInvite
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Email = dto.Email,
                Role = canonicalRole,
                InvitedBy = inviterUserId,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(InviteLifetime)
            };
            _context.DocumentPendingInvites.Add(pendingInvite);
            token = pendingInvite.Id;
        }
        await _context.SaveChangesAsync();

        // /invites/{token} works for everyone — sign-up flow detects
        // anonymity, redirects to sign-up with ?invite={token},
        // returns here after auth.
        var inviteUrl = $"{_emailSettings.BaseUrl}/invites/{token}";

        if (targetUser == null)
        {
            // Unregistered recipient — only the pending row exists
            // until they sign up + accept. Send the email.
            var pendingEmailSent = true;
            try
            {
                // No account yet, so no language of their own: the inviter's.
                await _emailService.SendDocumentInviteAsync(dto.Email, new EmailActor(inviterName, inviter?.Email), document.Title, dto.Role, inviteUrl, inviter?.Locale);
            }
            catch (Exception ex)
            {
                pendingEmailSent = false;
                _logger.LogError(ex, "Failed to send invite email to {Email} for document {DocumentId}", dto.Email, documentId);
            }

            return new InviteResultDto(true, false, dto.Email, pendingEmailSent ? _localizer["InvitationSent"].Value : "Invitation created but email delivery failed. Check server logs — likely the Resend API key is not set.");
        }

        // Registered recipient — add as collaborator now so the doc
        // appears in their list immediately, AND email so they get
        // the standard landing experience. Skipping the collaborator
        // row would force them to click the email link before the
        // doc shows up; that breaks the in-app notification flow.
        var result = await AddUserCollaboratorAsync(documentId, inviterUserId, new AddUserCollaboratorDto(targetUser.Id, canonicalRole));
        if (result == null)
            return new InviteResultDto(false, true, dto.Email, _localizer["FailedToAddCollaborator"].Value);

        // They have access now, so the invite is accepted: it no longer counts
        // as pending on the owner's Sharing page. One email, not two: adding the
        // collaborator publishes DocumentSharedEvent, whose handler sends
        // "… shared … with you" (10 Oct: an existing user got that AND
        // "… invited you to …" for the same share).
        var acceptedNow = await _context.DocumentPendingInvites.FindAsync(token);
        if (acceptedNow != null)
        {
            acceptedNow.Status = "accepted";
            await _context.SaveChangesAsync();
        }

        return new InviteResultDto(true, true, dto.Email, null);
    }

    public async Task<List<PendingInviteDto>> GetPendingInvitesAsync(Guid documentId)
    {
        var now = DateTime.UtcNow;
        var rows = await _context.DocumentPendingInvites.AsNoTracking()
            .Where(p => p.DocumentId == documentId && p.Status == "pending")
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
        // The last send is when the current lifetime started (never before the first).
        return rows.Select(p =>
        {
            var sent = p.ExpiresAt - InviteLifetime;
            return new PendingInviteDto(p.Id, p.Email, p.Role, p.ExpiresAt < now ? "expired" : "pending",
                p.CreatedAt, sent < p.CreatedAt ? p.CreatedAt : sent, p.ExpiresAt);
        }).ToList();
    }

    public async Task<InviteResultDto?> ResendInviteAsync(Guid documentId, Guid inviteId, string userId)
    {
        var invite = await _context.DocumentPendingInvites.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == inviteId && p.DocumentId == documentId && p.Status == "pending");
        if (invite == null) return null;
        // The invite path refreshes the same row (same link) and sends the email again.
        return await InviteByEmailAsync(documentId, userId, new InviteCollaboratorDto(invite.Email, invite.Role));
    }

    public async Task<bool> CancelInviteAsync(Guid documentId, Guid inviteId)
    {
        var invite = await _context.DocumentPendingInvites
            .FirstOrDefaultAsync(p => p.Id == inviteId && p.DocumentId == documentId && p.Status == "pending");
        if (invite == null) return false;
        invite.Status = "cancelled";
        await _context.SaveChangesAsync();
        return true;
    }
}
