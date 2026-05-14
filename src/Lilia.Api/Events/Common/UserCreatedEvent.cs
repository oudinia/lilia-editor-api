namespace Lilia.Api.Events.Common;

/// <summary>
/// Published when our system observes a brand-new user — currently emitted
/// by the Kinde webhook (<c>WebhooksController.Kinde</c>) on the
/// <c>user.created</c> event. This is the registration moment, not a
/// sign-in (<c>user.authenticated</c> is ignored).
///
/// Cross-slice fan-out via Wolverine: Teams subscribes to mint a default
/// team + send welcome; future slices (Onboarding, Analytics) can attach
/// their own handlers without touching the webhook code.
///
/// Idempotency contract: handlers MUST tolerate duplicate deliveries.
/// Kinde retries on non-2xx and may replay events. Treat this event as
/// "we'd like the system to be in a post-registration state for this
/// user" rather than "do exactly-once work".
/// </summary>
public record UserCreatedEvent(string UserId, string Email, string? FirstName);
