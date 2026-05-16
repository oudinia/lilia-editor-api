namespace Lilia.Api.Events.Common;

/// <summary>
/// Published when a team member invite is created via
/// <c>TeamService.InviteMemberAsync</c>. Carries the team's display name
/// (= codename for auto-minted teams), the inviter's display name, and
/// the invitee's email so the slice subscriber can send the "X invited
/// you to <team>" email without re-querying.
///
/// Idempotency: handlers MUST tolerate duplicates. The current
/// <c>InviteMemberAsync</c> upserts on (team_id, email), so repeated
/// invites to the same email re-fire this event — and a second email
/// is acceptable (UX nudge) rather than something to suppress.
/// </summary>
public record TeamInviteCreatedEvent(
    Guid TeamId,
    string TeamName,
    string InvitedByUserId,
    string? InvitedByName,
    string InvitedEmail,
    string Role);
