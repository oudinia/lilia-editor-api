using Lilia.Api.Events.Common;
using Lilia.Api.Services;

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
            await email.SendTeamMemberRemovedAsync(
                toEmail: evt.RemovedUserEmail,
                recipientName: evt.RemovedUserName,
                removerName: removerName,
                teamName: evt.TeamName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendTeamMemberRemovedAsync failed for {Email}", evt.RemovedUserEmail);
        }
    }
}
