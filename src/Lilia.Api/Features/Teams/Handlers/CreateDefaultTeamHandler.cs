using Lilia.Api.Events.Common;
using Lilia.Api.Features.Teams.Services;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Handlers;

/// <summary>
/// Wolverine handler: subscribes to <see cref="UserCreatedEvent"/> (published once by
/// <c>UserService</c> when it creates an account) and gives the person a default team with a
/// generated codename. Quiet since 21 Oct 2026: no email. "Your team is ready" belongs to teams
/// people create themselves; the welcome email is <c>WelcomeOnSignUpHandler</c>'s
/// (reports/2026-10-21-welcome-onboarding.md).
///
/// Idempotent: the team is minted only while <c>User.DefaultTeamId is null</c>, so a duplicate
/// delivery does nothing.
/// </summary>
public class CreateDefaultTeamHandler
{
    public async Task Handle(
        UserCreatedEvent evt,
        LiliaDbContext db,
        ITeamCodenameGenerator codenames,
        ILogger<CreateDefaultTeamHandler> logger,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == evt.UserId, ct);
        if (user is null)
        {
            logger.LogWarning("UserCreatedEvent: user {UserId} not in DB — skipping team mint", evt.UserId);
            return;
        }

        if (user.DefaultTeamId is not null)
        {
            logger.LogDebug("UserCreatedEvent: user {UserId} already has DefaultTeamId — skipping mint", evt.UserId);
            return;
        }

        // 1) Mint a unique codename. The pool is large (~28M); a single
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

        // 2) Create the team + default "Everyone" Group + owner
        //    membership in a single SaveChanges.
        //
        //    Note: the original implementation wrote to TeamMembers
        //    (a parallel membership table). That bypassed the Group
        //    abstraction the rest of the codebase uses for member
        //    reads — meaning auto-minted teams showed an empty
        //    member list in the UI, and SetDocumentTeamAsync
        //    refused attach-to-team for these teams. Mirroring the
        //    user-create path (TeamService.CreateTeamAsync) makes
        //    every team a Group-rooted structure with one source of
        //    truth (group_members). The TeamMember table was
        //    retired in the same PR.
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
        var defaultGroup = new Group
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Name = "Everyone",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
        };
        var ownership = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = defaultGroup.Id,
            UserId = evt.UserId,
            // Owner role's seed id from RoleConfiguration.HasData.
            // Hard-coded here for the same reason CreateTeamAsync
            // does its own ownerRole lookup — both work but the
            // seed id is stable forever and avoids a DB roundtrip.
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            CreatedAt = DateTime.UtcNow,
        };
        db.Teams.Add(team);
        db.Set<Group>().Add(defaultGroup);
        db.Set<GroupMember>().Add(ownership);
        user.DefaultTeamId = team.Id;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Default team {Codename} minted for user {UserId}", codename, evt.UserId);
    }

    // Slug = lowercase + dashes — keeps URLs readable while the codename
    // stays intact for display ("Cobalt Photon A7B" → "cobalt-photon-a7b").
    private static string SlugFromCodename(string codename) =>
        codename.Trim().ToLowerInvariant().Replace(' ', '-');
}
