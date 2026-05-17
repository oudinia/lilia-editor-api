using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Spec §8 + §13 (iter 7) — backs the /invites/{token} landing page
/// on the editor. Single mental model for both registered and
/// unregistered recipients:
///
///   GET  /api/invites/{token}        — public, returns who/what/why
///   POST /api/invites/{token}/accept — authed, materializes the
///                                       collaborator row, returns docId
///   DELETE /api/invites/{token}      — authed, marks invite declined
///
/// Token = DocumentPendingInvite.Id (Guid). InviteByEmailAsync was
/// updated in the companion change to always create a pending row,
/// even for users who are already collaborators, so this endpoint
/// works regardless of recipient state.
/// </summary>
[ApiController]
[Route("api/invites")]
public class InvitesController : ControllerBase
{
    private readonly LiliaDbContext _context;

    public InvitesController(LiliaDbContext context)
    {
        _context = context;
    }

    private string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet("{token:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<InviteResolveDto>> Resolve(Guid token)
    {
        var invite = await _context.DocumentPendingInvites
            .Include(i => i.Document)
            .Include(i => i.Inviter)
            .FirstOrDefaultAsync(i => i.Id == token);

        if (invite == null) return NotFound();

        // Expired invites still resolve so the landing page can show a
        // "this invite expired" state instead of a generic 404 — useful
        // for telling a recipient to ask the inviter for a fresh one.
        var status = invite.ExpiresAt < DateTime.UtcNow && invite.Status == "pending"
            ? "expired"
            : invite.Status;

        return Ok(new InviteResolveDto(
            invite.Id,
            invite.Email,
            invite.Document.Id,
            invite.Document.Title,
            invite.Inviter.Name ?? invite.Inviter.Email ?? "Someone",
            invite.Role,
            status,
            invite.CreatedAt,
            invite.ExpiresAt));
    }

    [HttpPost("{token:guid}/accept")]
    public async Task<ActionResult<InviteAcceptResultDto>> Accept(Guid token)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var invite = await _context.DocumentPendingInvites
            .FirstOrDefaultAsync(i => i.Id == token);
        if (invite == null) return NotFound();
        if (invite.ExpiresAt < DateTime.UtcNow)
            return Conflict(new { error = "This invite has expired." });

        // Verify the authed user's email matches the invite. Without
        // this check anyone with the token could accept on behalf of
        // the actual invitee. Email match is the binding contract for
        // a per-email invite — guard it server-side.
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();
        if (!string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var existingCollab = await _context.DocumentCollaborators
            .FirstOrDefaultAsync(c => c.DocumentId == invite.DocumentId && c.UserId == userId);
        if (existingCollab == null)
        {
            // Look up the role by canonical name — the role text on
            // the invite was already canonicalized by the inviter
            // path's RoleNames.Normalize, so a straight match works.
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == invite.Role);
            if (role == null) return Conflict(new { error = "Invite role no longer exists." });

            _context.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(),
                DocumentId = invite.DocumentId,
                UserId = userId,
                RoleId = role.Id,
                InvitedBy = invite.InvitedBy,
                CreatedAt = DateTime.UtcNow,
            });
        }

        invite.Status = "accepted";
        await _context.SaveChangesAsync();
        return Ok(new InviteAcceptResultDto(invite.DocumentId));
    }

    [HttpDelete("{token:guid}")]
    public async Task<ActionResult> Decline(Guid token)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var invite = await _context.DocumentPendingInvites.FindAsync(token);
        if (invite == null) return NotFound();

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();
        if (!string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        invite.Status = "declined";
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
