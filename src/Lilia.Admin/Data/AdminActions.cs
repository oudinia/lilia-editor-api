using System.Net.Http.Json;
using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Admin.Data;

public sealed record ActionResult(bool Ok, string Message);
public sealed record ActionRow(DateTime At, string AdminEmail, string Action, string? TargetUserId, string Reason, string? Details);
public sealed record PlanOption(string Slug, string Name);

/// <summary>
/// What support can do to an account (admin phase 2, 17 Oct): grant Ask Lilia credits, change the
/// plan, suspend and restore, resend the verification email. Each needs a reason and is written to
/// admin_actions in the same transaction as the change, with the operator's email; nothing reads or
/// changes a document.
/// </summary>
public sealed class AdminActions(IDbContextFactory<LiliaDbContext> factory, IHttpClientFactory http, IConfiguration config, TimeProvider clock)
{
    public const int MaxCredits = 10_000;
    public const string SuspendedReason = "suspended";

    private static string? CheckReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 ? "Write a reason (5 characters or more): it is kept with the action." : null;

    public async Task<ActionResult> GrantCreditsAsync(string admin, string userId, int amount, string? reason, CancellationToken ct = default)
    {
        if (CheckReason(reason) is { } bad) return new(false, bad);
        if (amount is < 1 or > MaxCredits) return new(false, $"Grant between 1 and {MaxCredits} credits.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return new(false, "No such account.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(), UserId = userId, Delta = amount, Reason = "grant",
            Note = $"admin: {reason!.Trim()}", CreatedAt = Now,
        });
        Log(db, admin, "grant_credits", userId, reason!, new { amount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, $"{amount} credits granted.");
    }

    public async Task<IReadOnlyList<PlanOption>> PlansAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Plans.Where(p => p.IsActive).OrderBy(p => p.MonthlyPrice ?? 0)
            .Select(p => new PlanOption(p.Slug, p.DisplayName)).ToListAsync(ct);
    }

    /// <summary>Ends the account's active plan and starts this one; "free" just ends it.</summary>
    public async Task<ActionResult> SetPlanAsync(string admin, string userId, string slug, string? reason, CancellationToken ct = default)
    {
        if (CheckReason(reason) is { } bad) return new(false, bad);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return new(false, "No such account.");
        var plan = slug == "free" ? null : await db.Plans.FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive, ct);
        if (slug != "free" && plan is null) return new(false, "No such plan.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await db.UserPlans.AsTracking().Where(p => p.UserId == userId && (p.Status == "active" || p.Status == "trial")).ToListAsync(ct);
        var before = current.Count == 0 ? "free" : string.Join(",", await db.Plans.Where(p => current.Select(c => c.PlanId).Contains(p.Id)).Select(p => p.Slug).ToListAsync(ct));
        foreach (var c in current) { c.Status = "cancelled"; c.EndsAt = Now; c.UpdatedAt = Now; }
        if (plan is not null)
            db.UserPlans.Add(new UserPlan
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "active", StartedAt = Now,
                ExternalRef = "admin", CreatedAt = Now, UpdatedAt = Now,
            });
        Log(db, admin, "set_plan", userId, reason!, new { from = before, to = slug });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, $"Plan changed from {before} to {slug}.");
    }

    /// <summary>Suspends or restores sign-in to the API (refused with ACCOUNT_SUSPENDED within 30 s).</summary>
    public async Task<ActionResult> SetSuspendedAsync(string admin, string userId, bool suspend, string? reason, CancellationToken ct = default)
    {
        if (CheckReason(reason) is { } bad) return new(false, bad);
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new(false, "No such account.");
        if (user.BanReason == "erased") return new(false, "This account is erased; there is nothing to restore.");
        if (suspend == (user.Banned && user.BanReason == SuspendedReason)) return new(false, suspend ? "Already suspended." : "Not suspended.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        user.Banned = suspend;
        user.BanReason = suspend ? SuspendedReason : null;
        user.BanExpires = null;
        user.UpdatedAt = Now;
        Log(db, admin, suspend ? "suspend" : "unsuspend", userId, reason!, null);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, suspend ? "Suspended: the API refuses this account within 30 seconds." : "Restored.");
    }

    /// <summary>
    /// Turns two-step sign-in off for someone who lost their phone and their email (25 Oct 2026; the
    /// owner was locked out the day before), and forgets the account's trusted devices. The API reads
    /// the flag with a 30 s cache, so the account gets in within half a minute.
    /// </summary>
    public async Task<ActionResult> TwoStepOffAsync(string admin, string userId, string? reason, CancellationToken ct = default)
    {
        if (CheckReason(reason) is { } bad) return new(false, bad);
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new(false, "No such account.");
        if (!user.TwoStepEnabled) return new(false, "Two-step sign-in is already off.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        user.TwoStepEnabled = false;
        user.UpdatedAt = Now;
        var devices = await db.TrustedDevices.AsTracking().Where(d => d.UserId == userId && d.RevokedAt == null).ToListAsync(ct);
        foreach (var d in devices) d.RevokedAt = Now;
        Log(db, admin, "two_step_off", userId, reason!, devices.Count > 0 ? new { forgottenDevices = devices.Count } : null);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, $"Two-step sign-in is off{(devices.Count > 0 ? $"; {devices.Count} trusted device(s) forgotten" : "")}. The account gets in within 30 seconds.");
    }

    /// <summary>Through the API's own endpoint, so it is the same email people get.</summary>
    public async Task<ActionResult> ResendVerificationAsync(string admin, string userId, string? reason, CancellationToken ct = default)
    {
        if (CheckReason(reason) is { } bad) return new(false, bad);
        await using var db = await factory.CreateDbContextAsync(ct);
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (email is null) return new(false, "No such account.");
        var api = (config["Admin:ApiBase"] ?? "http://127.0.0.1:5001").TrimEnd('/');
        var r = await http.CreateClient().PostAsJsonAsync($"{api}/api/auth/resend-verification", new { email }, ct);
        if (!r.IsSuccessStatusCode) return new(false, $"The API refused it ({(int)r.StatusCode}).");
        Log(db, admin, "resend_verification", userId, reason!, null);
        await db.SaveChangesAsync(ct);
        return new(true, "Verification email sent (it reaches people once real email is set up).");
    }

    public async Task<IReadOnlyList<ActionRow>> HistoryAsync(string? userId, int take = 100, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.AdminActions.AsQueryable();
        if (userId is not null) q = q.Where(a => a.TargetUserId == userId);
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync(ct);
        return rows.Select(a => new ActionRow(a.CreatedAt, a.AdminEmail, a.Action, a.TargetUserId, a.Reason, a.Details?.RootElement.GetRawText())).ToList();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    internal void Log(LiliaDbContext db, string admin, string action, string? userId, string reason, object? details) =>
        db.AdminActions.Add(new AdminAction
        {
            Id = Guid.NewGuid(), AdminEmail = admin, Action = action, TargetUserId = userId, Reason = reason.Trim(),
            Details = details is null ? null : JsonSerializer.SerializeToDocument(details), CreatedAt = Now,
        });
}
