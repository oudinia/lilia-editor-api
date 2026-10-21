using Microsoft.Extensions.Caching.Distributed;

namespace Lilia.Api.Services.Email;

/// <summary>
/// At most one sign-in, reset or verification email a minute and five an hour per address, across
/// every way one is asked for (the auth email routes and Stytch's webhook). Anyone can ask, and
/// behind the tunnel every caller has the same IP, so the limit is per address.
/// </summary>
public static class AuthEmailLimit
{
    /// <summary>Takes one send for <paramref name="email"/>; false when its limit is reached (send nothing).</summary>
    public static async Task<bool> TryTakeAsync(IDistributedCache? cache, string email, CancellationToken ct = default)
    {
        if (cache is null) return true;
        var who = email.Trim().ToLowerInvariant();
        var minuteKey = $"authmail:minute:{who}";
        var hourKey = $"authmail:hour:{who}";
        if (await cache.GetAsync(minuteKey, ct) is not null) return false;
        var hour = await cache.GetStringAsync(hourKey, ct);
        var sent = int.TryParse(hour, out var n) ? n : 0;
        if (sent >= 5) return false;
        await cache.SetAsync(minuteKey, [1], new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(55) }, ct);
        // The hour window starts at the first email and isn't extended by later ones.
        await cache.SetStringAsync(hourKey, (sent + 1).ToString(), new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) }, ct);
        return true;
    }
}
