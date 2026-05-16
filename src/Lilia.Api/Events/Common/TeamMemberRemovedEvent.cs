namespace Lilia.Api.Events.Common;

/// <summary>
/// Published when a member is removed from a team via
/// <c>TeamService.RemoveMemberAsync</c>. Carries enough context for
/// downstream handlers (currently SendRemovedEmailHandler) to fire a
/// notification without re-querying.
///
/// Idempotency: handlers MUST tolerate duplicate deliveries. Wolverine
/// local mode is fire-once but the same membership can be re-removed
/// after a re-add cycle, so a second email is a soft annoyance rather
/// than a defect.
/// </summary>
public record TeamMemberRemovedEvent(
    Guid TeamId,
    string TeamName,
    string RemovedByUserId,
    string? RemovedByName,
    string RemovedUserId,
    string? RemovedUserEmail,
    string? RemovedUserName);
