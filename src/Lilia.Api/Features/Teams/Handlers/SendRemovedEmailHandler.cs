using Lilia.Api.Events.Common;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Handlers;

/// <summary>
/// Wolverine handler — subscribes to <see cref="TeamMemberRemovedEvent"/>
/// (published by <c>TeamService.RemoveMemberAsync</c>) and emails the
/// removed user. Fire-and-log: a failed email never throws back to the
/// publisher, so the removal itself stays successful even when Resend
/// is down.
///
/// Skips silently when we don't have an email on file for the removed
/// user (rare — happens for legacy rows or M2M-only accounts). No
/// in-app notification yet; just the email.
/// </summary>
public class SendRemovedEmailHandler
{
    public async Task Handle(
        TeamMemberRemovedEvent evt,
        IEmailService email,
        LiliaDbContext db,
        ILogger<SendRemovedEmailHandler> logger)
    {
        if (string.IsNullOrEmpty(evt.RemovedUserEmail))
        {
            logger.LogDebug("TeamMemberRemovedEvent: no email on file for {UserId}, skipping removal email", evt.RemovedUserId);
            return;
        }

        var removerName = string.IsNullOrWhiteSpace(evt.RemovedByName) ? "The team owner" : evt.RemovedByName!;

        try
        {
            var people = await db.Users.AsNoTracking()
                .Where(u => u.Id == evt.RemovedUserId || u.Id == evt.RemovedByUserId)
                .Select(u => new { u.Id, u.Email, u.Locale }).ToListAsync();
            var to = people.FirstOrDefault(u => u.Id == evt.RemovedUserId);
            var by = people.FirstOrDefault(u => u.Id == evt.RemovedByUserId);
            await email.SendTeamMemberRemovedAsync(
                toEmail: evt.RemovedUserEmail,
                removedBy: new EmailActor(removerName, by?.Email),
                teamName: evt.TeamName,
                locale: to?.Locale ?? by?.Locale);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendTeamMemberRemovedAsync failed for {Email}", evt.RemovedUserEmail);
        }
    }
}
