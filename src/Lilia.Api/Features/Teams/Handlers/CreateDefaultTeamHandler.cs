using Lilia.Api.Events.Common;
using Lilia.Api.Features.Teams.Services;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Handlers;

/// <summary>
/// Wolverine handler — subscribes to <see cref="UserCreatedEvent"/> and
/// guarantees the user has a default team with a generated codename, then
/// fires the welcome email.
///
/// Why this lives in the Teams slice (not in WebhooksController):
/// the webhook is a transport concern; default-team minting is a Teams
/// behavior. Splitting it here means a future v2 of team minting (e.g.
/// invite-only signup, team-suggestion picker before mint) can change
/// without touching the webhook.
///
/// Idempotency: this handler runs the full sequence on every delivery,
/// but every step is a no-op if already done — user upsert is
/// idempotent, team mint is gated on <c>User.DefaultTeamId is null</c>,
/// and welcome-email send is fire-and-log. Duplicate Kinde deliveries
/// will at worst send two welcome emails (acceptable per the event
/// contract).
/// </summary>
public class CreateDefaultTeamHandler
{
    public async Task Handle(
        UserCreatedEvent evt,
        LiliaDbContext db,
        IUserService userService,
        ITeamCodenameGenerator codenames,
        IEmailService email,
        ILogger<CreateDefaultTeamHandler> logger,
        CancellationToken ct)
    {
        // 1) Ensure the user row exists. Webhook fires at registration —
        //    UserSyncMiddleware would create the row on first
        //    authenticated request, but we want the team minted *now*
        //    so it's ready before they land in the editor.
        await userService.CreateOrUpdateUserAsync(new CreateOrUpdateUserDto(
            evt.UserId, evt.Email, evt.FirstName, null));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == evt.UserId, ct);
        if (user is null)
        {
            logger.LogWarning("UserCreatedEvent: user {UserId} not in DB after upsert — skipping team mint", evt.UserId);
            return;
        }

        if (user.DefaultTeamId is not null)
        {
            logger.LogDebug("UserCreatedEvent: user {UserId} already has DefaultTeamId — skipping mint", evt.UserId);
            return;
        }

        // 2) Mint a unique codename. The pool is large (~28M); a single
        //    DB hit per attempt is fine. Cap retries to fail loud rather
        //    than spin forever if the index is broken.
        string codename = string.Empty;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var candidate = codenames.Generate();
            var taken = await db.Teams.AnyAsync(t => t.TeamCode == candidate, ct);
            if (!taken) { codename = candidate; break; }
        }
        if (string.IsNullOrEmpty(codename))
        {
            logger.LogError("Codename generation could not find a unique value after 5 attempts — aborting team mint for {UserId}", evt.UserId);
            return;
        }

        // 3) Create the team + owner membership in a single SaveChanges.
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = codename,
            TeamCode = codename,
            Slug = SlugFromCodename(codename),
            OwnerId = evt.UserId,
            Plan = "free",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var membership = new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            UserId = evt.UserId,
            Role = "owner",
            JoinedAt = DateTime.UtcNow,
        };
        db.Teams.Add(team);
        db.Set<TeamMember>().Add(membership);
        user.DefaultTeamId = team.Id;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Default team {Codename} minted for user {UserId}", codename, evt.UserId);

        // 4) Fire-and-log welcome email. Failures don't roll back the
        //    team — the user has it in the UI; we'll retry the email
        //    via a separate job if we ever add one.
        try
        {
            await email.SendTeamWelcomeAsync(evt.Email, evt.FirstName, codename);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendTeamWelcomeAsync failed for {Email}", evt.Email);
        }
    }

    // Slug = lowercase + dashes — keeps URLs readable while the codename
    // stays intact for display ("Cobalt Photon A7B" → "cobalt-photon-a7b").
    private static string SlugFromCodename(string codename) =>
        codename.Trim().ToLowerInvariant().Replace(' ', '-');
}
