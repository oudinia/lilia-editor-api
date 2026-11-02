using Lilia.Api.Directories;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Sharing's answer to <see cref="ISharingAccounts"/>. Claiming invites moved here from UserService
/// (2 Nov 2026, the bounded contexts, step 3), unchanged: it writes Sharing's tables.
/// </summary>
public sealed class SharingAccounts(LiliaDbContext db, ISharingNotifier? notifier = null) : ISharingAccounts
{
    public async Task ClaimInvitesAsync(string userId, string email, CancellationToken ct = default)
    {
        var pendingInvites = await db.DocumentPendingInvites
            .Where(pi => pi.Email == email && pi.Status == "pending" && pi.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        var joined = new List<(Guid DocumentId, string Inviter)>();
        foreach (var invite in pendingInvites)
        {
            // Find the role by name
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == invite.Role, ct);
            if (role == null) continue;

            // Check if already a collaborator
            var existing = await db.DocumentCollaborators
                .AnyAsync(dc => dc.DocumentId == invite.DocumentId && dc.UserId == userId, ct);
            if (existing)
            {
                invite.Status = "accepted";
                continue;
            }

            var collaborator = new DocumentCollaborator
            {
                Id = Guid.NewGuid(),
                DocumentId = invite.DocumentId,
                UserId = userId,
                RoleId = role.Id,
                InvitedBy = invite.InvitedBy,
                CreatedAt = DateTime.UtcNow
            };
            db.DocumentCollaborators.Add(collaborator);
            invite.Status = "accepted";
            joined.Add((invite.DocumentId, invite.InvitedBy));
        }

        if (pendingInvites.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        // Signing up accepted these: each owner hears about it (Olivia, 10 Oct).
        if (notifier is not null)
            foreach (var (documentId, inviter) in joined)
                await notifier.InviteAcceptedAsync(documentId, inviter, userId);
    }

    public async Task ForgetAsync(string userId, CancellationToken ct = default) =>
        db.DocumentCollaborators.RemoveRange(await db.DocumentCollaborators.Where(c => c.UserId == userId).ToListAsync(ct));
}
