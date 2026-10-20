using Lilia.Api.Events.Common;

namespace Lilia.Api.Features.Onboarding;

/// <summary>
/// Wolverine handler for <see cref="UserCreatedEvent"/>: opens the person's onboarding record and
/// sends the welcome email when the address is already known to be real (a Google sign-up). An
/// email-and-password sign-up gets it once they verify (POST /api/me/onboarding/verified).
/// </summary>
public class WelcomeOnSignUpHandler
{
    public async Task Handle(UserCreatedEvent evt, IOnboardingService onboarding, CancellationToken ct)
    {
        await onboarding.EnsureAsync(evt.UserId, newAccount: true, ct);
        await onboarding.TrySendWelcomeAsync(evt.UserId, ct);
    }
}
