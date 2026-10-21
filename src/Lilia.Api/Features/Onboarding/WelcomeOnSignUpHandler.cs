using Lilia.Api.Events.Common;

namespace Lilia.Api.Features.Onboarding;

/// <summary>
/// Wolverine handler for <see cref="UserCreatedEvent"/>: opens the person's onboarding record and
/// sends the welcome email when the address is already known to be real (a Google sign-up). An
/// email-and-password sign-up gets Lilia's confirmation email now, and the welcome once they confirm.
/// </summary>
public class WelcomeOnSignUpHandler
{
    public async Task Handle(UserCreatedEvent evt, IOnboardingService onboarding, CancellationToken ct)
    {
        await onboarding.EnsureAsync(evt.UserId, newAccount: true, ct);
        // A confirmed address (Google) gets the welcome; an unconfirmed one, Lilia's confirmation.
        if (!await onboarding.TrySendWelcomeAsync(evt.UserId, ct))
            await onboarding.TrySendConfirmationAsync(evt.UserId, ct);
    }
}
