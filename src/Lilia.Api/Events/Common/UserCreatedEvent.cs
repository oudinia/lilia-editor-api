namespace Lilia.Api.Events.Common;

/// <summary>
/// Published when our system observes a brand-new user. Currently
/// raised by the local user-sync path in <c>AuthMiddleware</c> on
/// first request from a user we don't yet have a row for. A future
/// Stytch webhook controller (TODO post-launch) can also publish this
/// event from the <c>user.created</c> Stytch hook.
///
/// Cross-slice fan-out via Wolverine: Teams subscribes to mint a default
/// team + send welcome; future slices (Onboarding, Analytics) can attach
/// their own handlers without touching the source code.
///
/// Idempotency contract: handlers MUST tolerate duplicate deliveries.
/// Treat this event as "we'd like the system to be in a
/// post-registration state for this user" rather than "do exactly-once
/// work".
/// </summary>
public record UserCreatedEvent(string UserId, string Email, string? FirstName);
