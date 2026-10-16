using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Admin.Data;

public sealed record Series(string Name, IReadOnlyList<int> Values);
public sealed record Kpi(string Name, long Value, long Previous)
{
    /// <summary>Change against the previous period of the same length, in %; null when there was nothing before.</summary>
    public double? Change => Previous == 0 ? null : 100.0 * (Value - Previous) / Previous;
}
public sealed record Slice(string Name, long Count, long Extra = 0);
public sealed record Cohort(DateOnly Week, int Size, IReadOnlyList<int?> ActivePercentByWeek);
public sealed record FunnelStep(string Name, int Count);
public sealed record Percentiles(int Samples, int P50, int P95);
public sealed record TopAuthor(string UserId, string Email, int ActiveDays, int Documents, long AskTokens);

public sealed record Stats(
    int Days, IReadOnlyList<DateOnly> DayAxis, IReadOnlyList<Series> Daily, IReadOnlyList<Kpi> Kpis,
    int Dau, int Wau, int Mau,
    IReadOnlyList<Cohort> Cohorts, IReadOnlyList<FunnelStep> Funnel,
    IReadOnlyList<Slice> AskByPurpose, IReadOnlyList<Slice> AskByModel, IReadOnlyList<Slice> Exports, IReadOnlyList<Slice> Imports,
    IReadOnlyList<Slice> Plans, Percentiles CompileMs, Percentiles AskMs, IReadOnlyList<TopAuthor> TopAuthors);

/// <summary>
/// The console's statistics: daily trends, active accounts, weekly cohorts, the first-use funnel,
/// what features are used, how fast. "Active" means the account did something that day: a saved
/// version, an Ask Lilia request, a compile, an import or export, a new document or an audited
/// action. Every query reads ids and dates only, never a document's content.
/// </summary>
public sealed class AdminStats(IDbContextFactory<LiliaDbContext> factory, TimeProvider clock)
{
    internal sealed record Act(string UserId, DateTime Day);

    public async Task<Stats> StatsAsync(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = clock.GetUtcNow().UtcDateTime.Date;
        var from = today.AddDays(-(days - 1));
        var prevFrom = from.AddDays(-days);
        // Cohorts look back 8 weeks; the active set covers both periods and the cohorts.
        var cohortFrom = StartOfWeek(today).AddDays(-7 * 7);
        var activeFrom = new[] { prevFrom, cohortFrom, today.AddDays(-29) }.Min();

        var activity = await ActivityAsync(db, activeFrom, ct);
        var axis = Enumerable.Range(0, days).Select(i => from.AddDays(i)).ToList();

        var signups = await DailyAsync(db.Users.Where(u => u.CreatedAt >= prevFrom).Select(u => u.CreatedAt), ct);
        var docs = await DailyAsync(db.Documents.Where(d => d.CreatedAt >= prevFrom && !d.IsPlayground).Select(d => d.CreatedAt), ct);
        var ask = await DailyAsync(db.AiRequests.Where(r => r.CreatedAt >= prevFrom).Select(r => r.CreatedAt), ct);
        var compiles = await db.LaTeXCompilationEvents.Where(e => e.OccurredAt >= prevFrom)
            .GroupBy(e => new { e.OccurredAt.Date, e.Success }).Select(g => new { g.Key.Date, g.Key.Success, N = g.Count() }).ToListAsync(ct);
        var jobs = await db.Jobs.Where(j => j.CreatedAt >= prevFrom)
            .GroupBy(j => new { j.CreatedAt.Date, Type = j.JobType.ToUpper() }).Select(g => new { g.Key.Date, g.Key.Type, N = g.Count() }).ToListAsync(ct);
        var askTokens = await db.AiRequests.Where(r => r.CreatedAt >= prevFrom)
            .GroupBy(r => r.CreatedAt.Date).Select(g => new { g.Key, T = g.Sum(r => (long)r.PromptTokens + r.CompletionTokens) }).ToListAsync(ct);

        int On(Dictionary<DateTime, int> d, DateTime day) => d.GetValueOrDefault(day);
        var activeByDay = activity.GroupBy(a => a.Day).ToDictionary(g => g.Key, g => g.Select(a => a.UserId).Distinct().Count());
        var ok = compiles.Where(c => c.Success).ToDictionary(c => c.Date, c => c.N);
        var failed = compiles.Where(c => !c.Success).ToDictionary(c => c.Date, c => c.N);
        var exports = jobs.Where(j => j.Type == "EXPORT").ToDictionary(j => j.Date, j => j.N);
        var imports = jobs.Where(j => j.Type == "IMPORT").ToDictionary(j => j.Date, j => j.N);

        var daily = new List<Series>
        {
            new("Sign-ups", axis.Select(d => On(signups, d)).ToList()),
            new("Active accounts", axis.Select(d => On(activeByDay, d)).ToList()),
            new("Documents created", axis.Select(d => On(docs, d)).ToList()),
            new("Ask Lilia requests", axis.Select(d => On(ask, d)).ToList()),
            new("Compiles", axis.Select(d => On(ok, d) + On(failed, d)).ToList()),
            new("Failed compiles", axis.Select(d => On(failed, d)).ToList()),
            new("Exports", axis.Select(d => On(exports, d)).ToList()),
            new("Imports", axis.Select(d => On(imports, d)).ToList()),
        };

        long Sum(Dictionary<DateTime, int> d, DateTime a, DateTime b) => d.Where(kv => kv.Key >= a && kv.Key < b).Sum(kv => (long)kv.Value);
        long Distinct(DateTime a, DateTime b) => activity.Where(x => x.Day >= a && x.Day < b).Select(x => x.UserId).Distinct().Count();
        var end = today.AddDays(1);
        var tokens = askTokens.ToDictionary(t => t.Key, t => t.T);
        var kpis = new List<Kpi>
        {
            new("Sign-ups", Sum(signups, from, end), Sum(signups, prevFrom, from)),
            new("Active accounts", Distinct(from, end), Distinct(prevFrom, from)),
            new("Documents created", Sum(docs, from, end), Sum(docs, prevFrom, from)),
            new("Ask Lilia requests", Sum(ask, from, end), Sum(ask, prevFrom, from)),
            new("Ask Lilia tokens", tokens.Where(t => t.Key >= from).Sum(t => t.Value), tokens.Where(t => t.Key >= prevFrom && t.Key < from).Sum(t => t.Value)),
            new("Compiles", Sum(ok, from, end) + Sum(failed, from, end), Sum(ok, prevFrom, from) + Sum(failed, prevFrom, from)),
            new("Exports", Sum(exports, from, end), Sum(exports, prevFrom, from)),
        };

        var users = await db.Users.Where(u => u.CreatedAt >= cohortFrom || u.CreatedAt >= from)
            .Select(u => new { u.Id, u.CreatedAt }).ToListAsync(ct);

        return new Stats(
            days, axis.Select(DateOnly.FromDateTime).ToList(), daily, kpis,
            Dau: (int)Distinct(today, end), Wau: (int)Distinct(today.AddDays(-6), end), Mau: (int)Distinct(today.AddDays(-29), end),
            Cohorts: Cohorts(users.Select(u => (u.Id, u.CreatedAt)).ToList(), activity, today),
            Funnel: await FunnelAsync(db, users.Where(u => u.CreatedAt >= from).Select(u => u.Id).ToList(), activity, ct),
            AskByPurpose: await Slices(db.AiRequests.Where(r => r.CreatedAt >= from).GroupBy(r => r.Purpose)
                .Select(g => new Slice(g.Key, g.Count(), g.Sum(r => (long)r.PromptTokens + r.CompletionTokens))), byExtra: false, ct),
            AskByModel: await Slices(db.AiRequests.Where(r => r.CreatedAt >= from).GroupBy(r => r.Model)
                .Select(g => new Slice(g.Key, g.Count(), g.Sum(r => (long)r.PromptTokens + r.CompletionTokens))), byExtra: true, ct),
            Exports: await Slices(db.Jobs.Where(j => j.CreatedAt >= from && j.JobType.ToUpper() == "EXPORT").GroupBy(j => j.TargetFormat ?? "?")
                .Select(g => new Slice(g.Key, g.Count(), g.Count(j => j.Status.ToLower() == "failed"))), byExtra: false, ct),
            Imports: await Slices(db.Jobs.Where(j => j.CreatedAt >= from && j.JobType.ToUpper() == "IMPORT").GroupBy(j => j.SourceFormat ?? "?")
                .Select(g => new Slice(g.Key, g.Count(), g.Count(j => j.Status.ToLower() == "failed"))), byExtra: false, ct),
            Plans: await PlansAsync(db, ct),
            CompileMs: Pct(await db.LaTeXCompilationEvents.Where(e => e.OccurredAt >= from && e.Success).Select(e => e.DurationMs).ToListAsync(ct)),
            AskMs: Pct(await db.AiRequests.Where(r => r.CreatedAt >= from && r.LatencyMs != null).Select(r => r.LatencyMs!.Value).ToListAsync(ct)),
            TopAuthors: await TopAsync(db, activity.Where(a => a.Day >= from).ToList(), from, ct));
    }

    /// <summary>(account, day) pairs: something the account did that day, from every timestamped source.</summary>
    private static async Task<List<Act>> ActivityAsync(LiliaDbContext db, DateTime from, CancellationToken ct)
    {
        var parts = new List<List<Act>>
        {
            await db.DocumentVersions.Where(v => v.CreatedAt >= from && v.CreatedBy != null)
                .Select(v => new { U = v.CreatedBy!, D = v.CreatedAt.Date }).Distinct().Select(x => new Act(x.U, x.D)).ToListAsync(ct),
            await db.AiRequests.Where(r => r.CreatedAt >= from)
                .Select(r => new { U = r.UserId, D = r.CreatedAt.Date }).Distinct().Select(x => new Act(x.U, x.D)).ToListAsync(ct),
            await db.LaTeXCompilationEvents.Where(e => e.OccurredAt >= from && e.UserId != null)
                .Select(e => new { U = e.UserId!, D = e.OccurredAt.Date }).Distinct().Select(x => new Act(x.U, x.D)).ToListAsync(ct),
            await db.Jobs.Where(j => j.CreatedAt >= from)
                .Select(j => new { U = j.UserId, D = j.CreatedAt.Date }).Distinct().Select(x => new Act(x.U, x.D)).ToListAsync(ct),
            await db.Documents.Where(d => d.CreatedAt >= from)
                .Select(d => new { U = d.OwnerId, D = d.CreatedAt.Date }).Distinct().Select(x => new Act(x.U, x.D)).ToListAsync(ct),
            await db.AuditLogs.Where(a => a.CreatedAt >= from)
                .Select(a => new { U = a.UserId, D = a.CreatedAt.Date }).Distinct().Select(x => new Act(x.U, x.D)).ToListAsync(ct),
        };
        return parts.SelectMany(p => p).Distinct().ToList();
    }

    private static async Task<Dictionary<DateTime, int>> DailyAsync(IQueryable<DateTime> stamps, CancellationToken ct) =>
        await stamps.GroupBy(t => t.Date).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

    /// <summary>The last 8 sign-up weeks: of each week's new accounts, the % active in each week after.</summary>
    internal static IReadOnlyList<Cohort> Cohorts(IReadOnlyList<(string Id, DateTime CreatedAt)> users, IReadOnlyList<Act> activity, DateTime today)
    {
        var thisWeek = StartOfWeek(today);
        var weeksActive = activity.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Select(a => StartOfWeek(a.Day)).ToHashSet());
        var result = new List<Cohort>();
        for (var w = 7; w >= 0; w--)
        {
            var week = thisWeek.AddDays(-7 * w);
            var members = users.Where(u => StartOfWeek(u.CreatedAt) == week).Select(u => u.Id).ToList();
            var cells = new List<int?>();
            for (var k = 0; k < 8; k++)
            {
                var target = week.AddDays(7 * k);
                if (target > thisWeek || members.Count == 0) { cells.Add(null); continue; }
                var active = members.Count(id => weeksActive.TryGetValue(id, out var ws) && ws.Contains(target));
                cells.Add((int)Math.Round(100.0 * active / members.Count));
            }
            result.Add(new Cohort(DateOnly.FromDateTime(week), members.Count, cells));
        }
        return result;
    }

    private static async Task<IReadOnlyList<FunnelStep>> FunnelAsync(LiliaDbContext db, List<string> newUsers, List<Act> activity, CancellationToken ct)
    {
        if (newUsers.Count == 0)
            return [new("Signed up", 0), new("Created a document", 0), new("Came back another day", 0), new("Asked Lilia", 0), new("Exported", 0)];
        var withDoc = await db.Documents.Where(d => newUsers.Contains(d.OwnerId) && !d.IsPlayground).Select(d => d.OwnerId).Distinct().CountAsync(ct);
        var returned = activity.Where(a => newUsers.Contains(a.UserId)).GroupBy(a => a.UserId).Count(g => g.Select(a => a.Day).Distinct().Count() >= 2);
        var asked = await db.AiRequests.Where(r => newUsers.Contains(r.UserId)).Select(r => r.UserId).Distinct().CountAsync(ct);
        var exported = await db.Jobs.Where(j => newUsers.Contains(j.UserId) && j.JobType.ToUpper() == "EXPORT").Select(j => j.UserId).Distinct().CountAsync(ct);
        return [new("Signed up", newUsers.Count), new("Created a document", withDoc), new("Came back another day", returned), new("Asked Lilia", asked), new("Exported", exported)];
    }

    // Sorted after the query: Npgsql can't order by a member of a record built in the projection.
    private static async Task<IReadOnlyList<Slice>> Slices(IQueryable<Slice> q, bool byExtra, CancellationToken ct)
    {
        var rows = await q.ToListAsync(ct);
        return (byExtra ? rows.OrderByDescending(r => r.Extra) : rows.OrderByDescending(r => r.Count)).ToList();
    }

    private static async Task<IReadOnlyList<Slice>> PlansAsync(LiliaDbContext db, CancellationToken ct)
    {
        var paid = await db.UserPlans.Where(p => p.Status == "active").Join(db.Plans, p => p.PlanId, pl => pl.Id, (p, pl) => pl.Slug)
            .GroupBy(s => s).Select(g => new Slice(g.Key, g.Count())).ToListAsync(ct);
        var total = await db.Users.CountAsync(ct);
        var onPlans = paid.Sum(s => s.Count);
        return [new Slice("free (no plan row)", Math.Max(0, total - onPlans)), .. paid.OrderByDescending(s => s.Count)];
    }

    private static async Task<IReadOnlyList<TopAuthor>> TopAsync(LiliaDbContext db, List<Act> activity, DateTime from, CancellationToken ct)
    {
        var top = activity.GroupBy(a => a.UserId).Select(g => new { Id = g.Key, Days = g.Select(a => a.Day).Distinct().Count() })
            .OrderByDescending(x => x.Days).Take(10).ToList();
        var ids = top.Select(t => t.Id).ToList();
        var emails = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var docs = await db.Documents.Where(d => ids.Contains(d.OwnerId) && d.DeletedAt == null).GroupBy(d => d.OwnerId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var tokens = await db.AiRequests.Where(r => ids.Contains(r.UserId) && r.CreatedAt >= from).GroupBy(r => r.UserId)
            .Select(g => new { g.Key, T = g.Sum(r => (long)r.PromptTokens + r.CompletionTokens) }).ToDictionaryAsync(x => x.Key, x => x.T, ct);
        return top.Select(t => new TopAuthor(t.Id, emails.GetValueOrDefault(t.Id, "(deleted)"), t.Days, docs.GetValueOrDefault(t.Id), tokens.GetValueOrDefault(t.Id))).ToList();
    }

    internal static Percentiles Pct(List<int> samples)
    {
        if (samples.Count == 0) return new(0, 0, 0);
        samples.Sort();
        int At(double q) => samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(q * samples.Count) - 1)];
        return new(samples.Count, At(0.50), At(0.95));
    }

    internal static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));   // Monday
}
