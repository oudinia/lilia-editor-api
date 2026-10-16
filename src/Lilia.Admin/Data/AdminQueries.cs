using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Admin.Data;

public sealed record DayCount(DateOnly Day, int Count);

public sealed record Overview(
    int Users, int NewUsers24h, int NewUsers7d, IReadOnlyList<DayCount> SignupsByDay,
    int ActiveAuthors7d, int DocumentsCreated7d,
    int AskRequests7d, long AskTokens7d, int AskFailed7d,
    int Compiles7d, int CompilesFailed7d, int ImportErrors7d,
    int JobsFailed7d, int JobsStuck, int FeedbackNew);

public sealed record UserRow(string Id, string Email, string? Name, DateTime CreatedAt, int Documents, DateTime? LastActive, string? Plan, bool DeletionRequested);

public sealed record DocumentRow(Guid Id, string Title, DateTime CreatedAt, DateTime UpdatedAt, bool Deleted);
public sealed record LedgerRow(DateTime At, int Delta, string Reason, string? Note);
public sealed record AuditRow(DateTime At, string Action, string EntityType, string? EntityId);
public sealed record CompileFailure(DateTime At, Guid? DocumentId, string? Category, string? Token);

public sealed record UserDetail(
    UserRow User, IReadOnlyList<DocumentRow> Documents,
    int AskRequests30d, long AskTokens30d, IReadOnlyList<LedgerRow> Ledger,
    IReadOnlyList<AuditRow> Audit, IReadOnlyList<CompileFailure> RecentCompileFailures);

public sealed record ErrorGroup(string Kind, string Detail, int Count, DateTime LastSeen, string? Example);
public sealed record FailedJob(DateTime At, string Type, string Status, string? Error, string UserId);
public sealed record Errors(IReadOnlyList<ErrorGroup> Compiles, IReadOnlyList<ErrorGroup> Imports, IReadOnlyList<FailedJob> Jobs);

public sealed record FeedbackRow(Guid Id, DateTime At, string Type, string Status, string Message, string? Page, string? UserId, string? DocumentId);

/// <summary>
/// What the admin console reads. Never a document's content or a block's text: titles, dates and
/// counts only (the Privacy Policy promises that the operator doesn't read documents).
/// </summary>
public sealed class AdminQueries(IDbContextFactory<LiliaDbContext> factory, TimeProvider clock)
{
    public static readonly string[] FeedbackStatuses = ["new", "acknowledged", "in-progress", "resolved", "dismissed"];

    public async Task<Overview> OverviewAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var d1 = now.AddDays(-1); var d7 = now.AddDays(-7); var d14 = now.Date.AddDays(-13);

        var signups = await db.Users.AsNoTracking().Where(u => u.CreatedAt >= d14)
            .GroupBy(u => u.CreatedAt.Date).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byDay = Enumerable.Range(0, 14).Select(i => d14.AddDays(i))
            .Select(day => new DayCount(DateOnly.FromDateTime(day), signups.FirstOrDefault(s => s.Key == day)?.Count ?? 0))
            .ToList();

        var ask = db.AiRequests.AsNoTracking().Where(r => r.CreatedAt >= d7);
        var compiles = db.LaTeXCompilationEvents.AsNoTracking().Where(e => e.OccurredAt >= d7);
        var stuckBefore = now.AddMinutes(-30);

        return new Overview(
            Users: await db.Users.CountAsync(ct),
            NewUsers24h: await db.Users.CountAsync(u => u.CreatedAt >= d1, ct),
            NewUsers7d: await db.Users.CountAsync(u => u.CreatedAt >= d7, ct),
            SignupsByDay: byDay,
            ActiveAuthors7d: await db.Documents.AsNoTracking().Where(d => d.UpdatedAt >= d7 && d.DeletedAt == null)
                .Select(d => d.OwnerId).Distinct().CountAsync(ct),
            DocumentsCreated7d: await db.Documents.CountAsync(d => d.CreatedAt >= d7 && !d.IsPlayground, ct),
            AskRequests7d: await ask.CountAsync(ct),
            AskTokens7d: await ask.SumAsync(r => (long)r.PromptTokens + r.CompletionTokens, ct),
            AskFailed7d: await ask.CountAsync(r => r.ErrorMessage != null || r.Status == "failed", ct),
            Compiles7d: await compiles.CountAsync(ct),
            CompilesFailed7d: await compiles.CountAsync(e => !e.Success, ct),
            ImportErrors7d: await db.ImportTelemetryEvents.CountAsync(e => e.CreatedAt >= d7 && e.Severity == "error", ct),
            JobsFailed7d: await db.Jobs.CountAsync(j => j.CreatedAt >= d7 && j.Status.ToLower() == "failed", ct),
            JobsStuck: await db.Jobs.CountAsync(j => (j.Status.ToLower() == "pending" || j.Status.ToLower() == "processing") && j.UpdatedAt < stuckBefore, ct),
            FeedbackNew: await db.Feedback.CountAsync(f => f.Status == "new", ct));
    }

    /// <summary>Accounts, newest first; <paramref name="q"/> matches the email, the name or the id.</summary>
    public async Task<IReadOnlyList<UserRow>> UsersAsync(string? q, int take = 50, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            users = users.Where(u => EF.Functions.ILike(u.Email, like) || (u.Name != null && EF.Functions.ILike(u.Name, like)) || u.Id == q.Trim());
        }
        return await Rows(db, users.OrderByDescending(u => u.CreatedAt).Take(take), ct);
    }

    public async Task<UserDetail?> UserAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = (await Rows(db, db.Users.AsNoTracking().Where(u => u.Id == id), ct)).FirstOrDefault();
        if (row is null) return null;
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-30);
        var ask = db.AiRequests.AsNoTracking().Where(r => r.UserId == id && r.CreatedAt >= since);
        return new UserDetail(
            row,
            await db.Documents.AsNoTracking().Where(d => d.OwnerId == id).OrderByDescending(d => d.UpdatedAt).Take(100)
                .Select(d => new DocumentRow(d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.DeletedAt != null)).ToListAsync(ct),
            await ask.CountAsync(ct),
            await ask.SumAsync(r => (long)r.PromptTokens + r.CompletionTokens, ct),
            await db.AiCreditLedger.AsNoTracking().Where(l => l.UserId == id).OrderByDescending(l => l.CreatedAt).Take(20)
                .Select(l => new LedgerRow(l.CreatedAt, l.Delta, l.Reason, l.Note)).ToListAsync(ct),
            await db.AuditLogs.AsNoTracking().Where(a => a.UserId == id).OrderByDescending(a => a.CreatedAt).Take(30)
                .Select(a => new AuditRow(a.CreatedAt, a.Action, a.EntityType, a.EntityId)).ToListAsync(ct),
            await db.LaTeXCompilationEvents.AsNoTracking().Where(e => e.UserId == id && !e.Success).OrderByDescending(e => e.OccurredAt).Take(10)
                .Select(e => new CompileFailure(e.OccurredAt, e.DocumentId, e.ErrorCategory, e.ErrorToken)).ToListAsync(ct));
    }

    /// <summary>The last week's failures, grouped: compiles by category and token, imports by kind, failed jobs.</summary>
    public async Task<Errors> ErrorsAsync(int days = 7, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-days);
        var compiles = await db.LaTeXCompilationEvents.AsNoTracking().Where(e => e.OccurredAt >= since && !e.Success)
            .GroupBy(e => new { e.ErrorCategory, e.ErrorToken })
            .Select(g => new { g.Key.ErrorCategory, g.Key.ErrorToken, Count = g.Count(), Last = g.Max(e => e.OccurredAt), Doc = g.Max(e => e.DocumentId.ToString()) })
            .OrderByDescending(g => g.Count).Take(25).ToListAsync(ct);
        var imports = await db.ImportTelemetryEvents.AsNoTracking().Where(e => e.CreatedAt >= since && e.Severity == "error")
            .GroupBy(e => new { e.EventKind, e.TokenOrEnv })
            .Select(g => new { g.Key.EventKind, g.Key.TokenOrEnv, Count = g.Count(), Last = g.Max(e => e.CreatedAt), Doc = g.Max(e => e.DocumentId.ToString()) })
            .OrderByDescending(g => g.Count).Take(25).ToListAsync(ct);
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.CreatedAt >= since && j.Status.ToLower() == "failed")
            .OrderByDescending(j => j.CreatedAt).Take(25)
            .Select(j => new FailedJob(j.CreatedAt, j.JobType, j.Status, j.ErrorMessage, j.UserId)).ToListAsync(ct);
        return new Errors(
            compiles.Select(g => new ErrorGroup(g.ErrorCategory ?? "unknown", g.ErrorToken ?? "", g.Count, g.Last, g.Doc)).ToList(),
            imports.Select(g => new ErrorGroup(g.EventKind, g.TokenOrEnv ?? "", g.Count, g.Last, g.Doc)).ToList(),
            jobs);
    }

    public async Task<IReadOnlyList<FeedbackRow>> FeedbackAsync(string? status, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Feedback.AsNoTracking();
        if (!string.IsNullOrEmpty(status)) q = q.Where(f => f.Status == status);
        return await q.OrderByDescending(f => f.CreatedAt).Take(100)
            .Select(f => new FeedbackRow(f.Id, f.CreatedAt, f.Type, f.Status, f.Message, f.Page, f.UserId, f.DocumentId)).ToListAsync(ct);
    }

    /// <summary>Triage: the one write in phase 1. Returns false for an unknown id or status.</summary>
    public async Task<bool> SetFeedbackStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        if (!FeedbackStatuses.Contains(status)) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        // Tracked on purpose: the console's contexts are no-tracking (read-only by default), and
        // without it this change was never saved.
        var f = await db.Feedback.AsTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return false;
        f.Status = status;
        f.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task<IReadOnlyList<UserRow>> Rows(LiliaDbContext db, IQueryable<Lilia.Core.Entities.User> users, CancellationToken ct)
    {
        var list = await users.Select(u => new
        {
            u.Id, u.Email, u.Name, u.CreatedAt, Deletion = u.DeletionRequestedAt != null,
            Documents = db.Documents.Count(d => d.OwnerId == u.Id && d.DeletedAt == null),
            LastActive = db.Documents.Where(d => d.OwnerId == u.Id).Max(d => (DateTime?)d.UpdatedAt),
            Plan = db.UserPlans.Where(p => p.UserId == u.Id && p.Status == "active")
                .Join(db.Plans, p => p.PlanId, pl => pl.Id, (p, pl) => pl.Slug).FirstOrDefault(),
        }).ToListAsync(ct);
        return list.Select(u => new UserRow(u.Id, u.Email, u.Name, u.CreatedAt, u.Documents, u.LastActive, u.Plan, u.Deletion)).ToList();
    }
}
