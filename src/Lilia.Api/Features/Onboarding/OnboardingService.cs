using Lilia.Api.Services;
using Lilia.Api.Services.Email;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Onboarding;

/// <summary>What the web needs to run the first run, the tour and the first wins.</summary>
public sealed record OnboardingDto(
    DateTime CreatedAt,
    bool Verified,
    bool WelcomeSent,
    DateTime? FirstDocumentAt,
    string? FirstDocumentRoute,
    int? TourStepReached,
    bool TourSeen,
    DateTime? FirstValidatePassAt,
    DateTime? FirstExportAt,
    bool TipsOptOut,
    bool Legacy,
    /// <summary>This call recorded the step for the first time: show its first-win line.</summary>
    bool JustRecorded = false);

/// <summary>Body of POST /api/me/onboarding/{step}; each step reads what it needs.</summary>
public sealed record OnboardingStepRequest(string? Route = null, int? Step = null, bool? OptOut = null,
    string? Name = null, string? TimeZone = null);

public interface IOnboardingService
{
    /// <summary>The person's onboarding row, created on first need. <paramref name="newAccount"/>: from the sign-up itself.</summary>
    Task<UserOnboarding?> EnsureAsync(string userId, bool newAccount = false, CancellationToken ct = default);
    Task<OnboardingDto?> GetAsync(string userId, CancellationToken ct = default);
    /// <summary>Records one of <see cref="OnboardingSteps.All"/>; null when the step or the user is unknown.</summary>
    Task<OnboardingDto?> RecordAsync(string userId, string step, OnboardingStepRequest? request, CancellationToken ct = default);
    /// <summary>Sends the welcome email once the address is known to be real; at most once per account.</summary>
    Task<bool> TrySendWelcomeAsync(string userId, CancellationToken ct = default);
    /// <summary>
    /// For a new account whose address Stytch hasn't confirmed: Lilia's "Confirm your email", in the
    /// account's language, under the per-address limit (so a sign-up page that also asks sends one).
    /// </summary>
    Task<bool> TrySendConfirmationAsync(string userId, CancellationToken ct = default);
    /// <summary>Keeps <c>User.Locale</c> on the interface language (en, fr, es) the person uses.</summary>
    Task SetLocaleAsync(string userId, string? locale, CancellationToken ct = default);
}

public static class OnboardingSteps
{
    public const string Start = "start";
    public const string Verified = "verified";
    public const string FirstDocument = "first-document";
    public const string TourStep = "tour-step";
    public const string TourSeen = "tour-seen";
    public const string TourRestart = "tour-restart";
    public const string FirstValidatePass = "first-validate-pass";
    public const string FirstExport = "first-export";
    public const string TipsOptOut = "tips-opt-out";

    public static readonly string[] All =
        [Start, Verified, FirstDocument, TourStep, TourSeen, TourRestart, FirstValidatePass, FirstExport, TipsOptOut];

    public static readonly string[] Routes = ["blank", "template", "import", "describe", "shared"];

    /// <summary>
    /// Accounts created before this (21 Oct 2026, midnight in Paris) have no welcome, no follow-up and
    /// no forced tour.
    /// </summary>
    public static readonly DateTime LaunchedAt = new(2026, 10, 20, 22, 0, 0, DateTimeKind.Utc);
}

/// <summary>
/// The server side of the welcome and onboarding process (reports/2026-10-21-welcome-onboarding.md):
/// one <see cref="UserOnboarding"/> row per account, timestamps only, never content.
/// </summary>
public sealed class OnboardingService(
    LiliaDbContext db,
    IEmailService email,
    ILogger<OnboardingService> logger,
    IStytchUserLookup? stytch = null,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache? cache = null,
    EmailSettings? emailSettings = null) : IOnboardingService
{
    /// <summary>
    /// The browser can't be relied on to ask for it: a returning browser runs its cached copy of the
    /// app for a few seconds after a deploy (21 Oct, the owner's own sign-up). So the server does.
    /// </summary>
    public async Task<bool> TrySendConfirmationAsync(string userId, CancellationToken ct = default)
    {
        if (stytch is not { CanAsk: true } || !userId.StartsWith("user-", StringComparison.Ordinal)) return false;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.Email) || StytchUserLookup.IsPlaceholder(user.Email)) return false;
        var info = await stytch.FindAsync(userId, ct);
        if (info is null || info.EmailVerified) return false;
        if (!await Lilia.Api.Services.Email.AuthEmailLimit.TryTakeAsync(cache, user.Email, ct)) return false;
        var token = await stytch.MintMagicLinkAsync(userId, 7 * 24 * 60, ct);
        if (token is null) return false;
        var origin = (emailSettings?.BaseUrl ?? "https://editor.liliaeditor.com").TrimEnd('/');
        var url = $"{origin}/auth/callback?stytch_token_type=magic_links&token={Uri.EscapeDataString(token)}&intent=verify";
        await email.SendStytchVerificationAsync(user.Email, url, user.Locale);
        logger.LogInformation("[Onboarding] confirmation email sent to {UserId} ({Locale})", userId, EmailTemplates.Language(user.Locale));
        return true;
    }

    public async Task<UserOnboarding?> EnsureAsync(string userId, bool newAccount = false, CancellationToken ct = default)
    {
        var row = await db.UserOnboarding.FirstOrDefaultAsync(o => o.UserId == userId, ct);
        if (row is not null) return row;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        row = new UserOnboarding { UserId = userId, CreatedAt = user.CreatedAt };
        if (!newAccount && user.CreatedAt < OnboardingSteps.LaunchedAt)
        {
            // An account from before onboarding existed: it never gets the welcome or the
            // follow-up, and the tour waits for Help → Take the tour.
            row.WelcomeSentAt = row.FollowUpSentAt = row.TourSeenAt = DateTime.UtcNow;
            row.VerifiedAt = user.CreatedAt;
        }
        db.UserOnboarding.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests at once: the other one created it.
            db.Entry(row).State = EntityState.Detached;
            row = await db.UserOnboarding.FirstOrDefaultAsync(o => o.UserId == userId, ct);
        }
        return row;
    }

    public async Task<OnboardingDto?> GetAsync(string userId, CancellationToken ct = default)
    {
        var row = await EnsureAsync(userId, ct: ct);
        if (row is null) return null;
        if (row.WelcomeSentAt is null && await TrySendWelcomeAsync(userId, ct))
            await db.Entry(row).ReloadAsync(ct);
        return ToDto(row);
    }

    public async Task<OnboardingDto?> RecordAsync(string userId, string step, OnboardingStepRequest? request, CancellationToken ct = default)
    {
        if (!OnboardingSteps.All.Contains(step)) return null;
        var row = await EnsureAsync(userId, ct: ct);
        if (row is null) return null;
        var now = DateTime.UtcNow;
        var first = false;

        switch (step)
        {
            case OnboardingSteps.Start:
                // From the web right after sign-up: the name typed there, and the browser's time zone.
                if (!string.IsNullOrWhiteSpace(request?.TimeZone) && request.TimeZone.Length <= 64)
                    row.TimeZone ??= request.TimeZone.Trim();
                if (!string.IsNullOrWhiteSpace(request?.Name))
                {
                    var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                    if (string.IsNullOrWhiteSpace(user.Name))
                        user.Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 120)];
                }
                break;
            case OnboardingSteps.Verified:
                await db.SaveChangesAsync(ct);
                await TrySendWelcomeAsync(userId, ct);
                await db.Entry(row).ReloadAsync(ct);
                return ToDto(row);
            case OnboardingSteps.FirstDocument:
                if (row.FirstDocumentAt is null)
                {
                    row.FirstDocumentAt = now;
                    var route = request?.Route?.Trim().ToLowerInvariant();
                    row.FirstDocumentRoute = route is not null && OnboardingSteps.Routes.Contains(route) ? route : null;
                    first = true;
                }
                break;
            case OnboardingSteps.TourStep:
                if (request?.Step is >= 1 and <= 4 && request.Step > (row.TourStepReached ?? 0))
                    row.TourStepReached = request.Step;
                break;
            case OnboardingSteps.TourSeen:
                if (row.TourSeenAt is null) { row.TourSeenAt = now; first = true; }
                break;
            case OnboardingSteps.TourRestart:
                // Help → Take the tour: counted; TourSeenAt stays, so it never comes back on its own.
                row.TourRestarts++;
                break;
            case OnboardingSteps.FirstValidatePass:
                if (row.FirstValidatePassAt is null) { row.FirstValidatePassAt = now; first = true; }
                break;
            case OnboardingSteps.FirstExport:
                if (row.FirstExportAt is null) { row.FirstExportAt = now; first = true; }
                break;
            case OnboardingSteps.TipsOptOut:
                row.TipsOptOut = request?.OptOut ?? true;
                break;
        }
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToDto(row) with { JustRecorded = first };
    }

    public async Task<bool> TrySendWelcomeAsync(string userId, CancellationToken ct = default)
    {
        var row = await EnsureAsync(userId, ct: ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null || user is null || row.WelcomeSentAt is not null) return false;
        if (string.IsNullOrWhiteSpace(user.Email) || StytchUserLookup.IsPlaceholder(user.Email)
            || user.DeletionRequestedAt is not null || user.Banned)
            return false;

        if (row.VerifiedAt is null)
        {
            // Stytch says whether the address is verified (Google sign-ups always are). Without a
            // Stytch secret (dev, tests) every address counts as real.
            if (stytch is { CanAsk: true } && userId.StartsWith("user-", StringComparison.Ordinal))
            {
                var info = await stytch.FindAsync(userId, ct);
                if (info is not { EmailVerified: true }) return false;
            }
            await db.UserOnboarding.Where(o => o.UserId == userId && o.VerifiedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.VerifiedAt, DateTime.UtcNow), ct);
        }

        // Claim the send first, so two requests at once can't both send it.
        var claimed = await db.UserOnboarding.Where(o => o.UserId == userId && o.WelcomeSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.WelcomeSentAt, DateTime.UtcNow), ct);
        if (claimed == 0) return false;
        try
        {
            await email.SendWelcomeAsync(user.Email, user.Name, user.Locale);
            logger.LogInformation("[Onboarding] welcome email sent to {UserId} ({Locale})", userId, EmailTemplates.Language(user.Locale));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Onboarding] welcome email to {UserId} failed; it will be tried again", userId);
            await db.UserOnboarding.Where(o => o.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.WelcomeSentAt, (DateTime?)null), ct);
            return false;
        }
    }

    public async Task SetLocaleAsync(string userId, string? locale, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(locale)) return;
        var lang = EmailTemplates.Language(locale);
        // Only the three interface languages are kept; "de-DE" from a browser says nothing about them.
        if (!locale.Trim().StartsWith(lang, StringComparison.OrdinalIgnoreCase)) return;
        await db.Users.Where(u => u.Id == userId && (u.Locale == null || u.Locale != lang))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Locale, lang), ct);
    }

    private static OnboardingDto ToDto(UserOnboarding o) => new(
        o.CreatedAt, o.VerifiedAt is not null, o.WelcomeSentAt is not null, o.FirstDocumentAt, o.FirstDocumentRoute,
        o.TourStepReached, o.TourSeenAt is not null, o.FirstValidatePassAt, o.FirstExportAt, o.TipsOptOut,
        Legacy: o.CreatedAt < OnboardingSteps.LaunchedAt);
}
