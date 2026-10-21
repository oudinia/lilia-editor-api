using System.Security.Cryptography;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Onboarding;

/// <summary>
/// The one follow-up (owner's yes, 21 Oct 2026): three days after sign-up, someone who confirmed
/// their address and has neither started a document nor opened one shared with them gets
/// "Three ways to start your first document", once, at 10:00 in their own time zone. Never after
/// "Don't send me tips", never to an account from before onboarding, closing or banned, and never
/// past day 10. reports/2026-10-21-welcome-onboarding.md
/// </summary>
public sealed class OnboardingFollowUp(LiliaDbContext db, IEmailService email, TimeProvider clock, ILogger<OnboardingFollowUp> logger,
    EmailSettings settings)
{
    public static readonly TimeSpan After = TimeSpan.FromDays(3);
    public static readonly TimeSpan Until = TimeSpan.FromDays(10);
    public const int LocalHour = 10;
    public const int MaxPerRun = 250;
    public const string DefaultTimeZone = "Europe/Paris";

    /// <summary>Sends every follow-up due now; returns how many went out.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var newest = now - After;
        var oldest = now - Until;
        var due = await db.UserOnboarding.AsNoTracking()
            .Where(o => o.FollowUpSentAt == null && !o.TipsOptOut && o.FirstDocumentAt == null && o.VerifiedAt != null
                && o.CreatedAt <= newest && o.CreatedAt >= oldest && o.CreatedAt >= OnboardingSteps.LaunchedAt)
            .Join(db.Users.Where(u => u.DeletionRequestedAt == null && !u.Banned), o => o.UserId, u => u.Id,
                (o, u) => new { o.UserId, o.TimeZone, u.Email, u.Name, u.Locale })
            // Nothing of their own, nothing shared with them: they really haven't started.
            .Where(x => !db.Documents.IgnoreQueryFilters().Any(d => d.OwnerId == x.UserId)
                && !db.DocumentCollaborators.Any(c => c.UserId == x.UserId))
            .OrderBy(x => x.UserId)
            .Take(MaxPerRun * 4)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var x in due)
        {
            if (sent >= MaxPerRun) break;
            if (StytchUserLookup.IsPlaceholder(x.Email) || LocalTime(now, x.TimeZone).Hour != LocalHour) continue;

            // Claim it first: two runs (or two servers) never send it twice.
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            var claimed = await db.UserOnboarding.Where(o => o.UserId == x.UserId && o.FollowUpSentAt == null && !o.TipsOptOut)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.FollowUpSentAt, now).SetProperty(o => o.TipsToken, token), ct);
            if (claimed == 0) continue;
            try
            {
                await email.SendGettingStartedAsync(x.Email, x.Name, x.Locale, TipsUrl(settings.BaseUrl, token));
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Onboarding] follow-up to {UserId} failed; it will be tried again", x.UserId);
                await db.UserOnboarding.Where(o => o.UserId == x.UserId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.FollowUpSentAt, (DateTime?)null), ct);
            }
        }
        if (sent > 0) logger.LogInformation("[Onboarding] {Count} follow-up email(s) sent", sent);
        return sent;
    }

    /// <summary>The one-click "Don't send me tips" address (also the List-Unsubscribe header).</summary>
    public static string TipsUrl(string baseUrl, string token) =>
        $"{baseUrl.TrimEnd('/')}/api/onboarding/tips-off?t={Uri.EscapeDataString(token)}";

    /// <summary>The time where the person is: their browser's zone at sign-up, else Paris.</summary>
    public static DateTime LocalTime(DateTime utc, string? timeZone)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? DefaultTimeZone)); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone));
        }
    }
}

/// <summary>Runs <see cref="OnboardingFollowUp"/> every 20 minutes, so each local 10:00 hour is seen.</summary>
public sealed class OnboardingFollowUpBackgroundService(IServiceScopeFactory scopes, ILogger<OnboardingFollowUpBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(20));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OnboardingFollowUp>().SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[Onboarding] follow-up run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
