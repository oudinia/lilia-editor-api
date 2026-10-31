using Lilia.Api.Events.Common;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Handlers;

/// <summary>
/// Wolverine handler — subscribes to <see cref="TeamInviteCreatedEvent"/>
/// (published by <c>TeamService.InviteMemberAsync</c>) and sends the
/// "X invited you to <team>" email. Fire-and-log so a Resend outage
/// doesn't roll back the invite itself.
///
/// The "accept" URL currently points at the team detail page; once we
/// have a tokenised accept flow it'll switch to that.
/// </summary>
public class SendInviteEmailHandler
{
    public async Task Handle(
        TeamInviteCreatedEvent evt,
        IEmailService email,
        EmailSettings settings,
        LiliaDbContext db,
        ILogger<SendInviteEmailHandler> logger)
    {
        var inviterName = string.IsNullOrWhiteSpace(evt.InvitedByName) ? "A teammate" : evt.InvitedByName!;
        // Until the tokenised /accept-invite flow ships the email
        // bounces users to the team page; existing members land on it
        // directly, new ones get redirected to sign-up first by the
        // app's auth gate.
        var acceptUrl = $"{settings.BaseUrl}/teams/{evt.TeamId}";

        try
        {
            // Their language when they have an account, else the inviter's.
            var invited = evt.InvitedEmail.Trim().ToLower();
            var to = await db.Users.AsNoTracking().Where(u => u.Email.ToLower() == invited)
                .Select(u => new { u.Locale }).FirstOrDefaultAsync();
            var by = await db.Users.AsNoTracking().Where(u => u.Id == evt.InvitedByUserId)
                .Select(u => new { u.Email, u.Locale }).FirstOrDefaultAsync();
            await email.SendTeamInviteAsync(
                toEmail: evt.InvitedEmail,
                inviter: new EmailActor(inviterName, by?.Email),
                teamName: evt.TeamName,
                role: evt.Role,
                acceptUrl: acceptUrl,
                locale: to?.Locale ?? by?.Locale);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendTeamInviteAsync failed for {Email}", evt.InvitedEmail);
        }
    }
}
