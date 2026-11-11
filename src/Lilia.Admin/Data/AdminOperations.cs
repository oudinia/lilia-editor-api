using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Admin.Data;

/// <summary>A kind of operation as the console shows it, with its use over the period.</summary>
public sealed record OperationKindRow(string Code, string Category, string Label, int Points, int? PointsPerUnit, int? UnitSize,
    string? UnitName, bool Active, int Count, int TotalPoints);

/// <summary>Points per day over the period.</summary>
public sealed record OperationDay(DateOnly Day, int Count, int Points);

/// <summary>The accounts that used the most points over the period.</summary>
public sealed record OperationAccount(string UserId, string? Email, int Count, int Points);

public sealed record OperationsOverview(int Days, int Count, int Points, IReadOnlyList<OperationKindRow> Kinds,
    IReadOnlyList<OperationDay> PerDay, IReadOnlyList<OperationAccount> TopAccounts);

/// <summary>
/// Operations weighted by complexity (the owner, 11 Nov 2026: "measurable as a new module with its database, and even an
/// admin section"): what authors did and what it weighed, by kind, day and account, and the weights, which an admin can
/// change. A change applies to operations recorded after it (each keeps the points it was given) and is logged.
/// </summary>
public sealed class AdminOperations(IDbContextFactory<LiliaDbContext> factory, TimeProvider clock)
{
    public async Task<OperationsOverview> OverviewAsync(int days = 30, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var since = clock.GetUtcNow().UtcDateTime.Date.AddDays(-(days - 1));
        var ops = db.Operations.AsNoTracking().Where(o => o.CreatedAt >= since);

        var byKind = await ops.GroupBy(o => o.Kind).Select(g => new { Kind = g.Key, Count = g.Count(), Points = g.Sum(o => o.Points) }).ToListAsync(ct);
        var kinds = (await db.OperationKinds.AsNoTracking().OrderBy(k => k.Category).ThenBy(k => k.Points).ToListAsync(ct))
            .Select(k =>
            {
                var used = byKind.FirstOrDefault(b => b.Kind == k.Code);
                return new OperationKindRow(k.Code, k.Category, k.Label, k.Points, k.PointsPerUnit, k.UnitSize, k.UnitName, k.Active,
                    used?.Count ?? 0, used?.Points ?? 0);
            }).ToList();

        var perDay = (await ops.GroupBy(o => o.CreatedAt.Date).Select(g => new { Day = g.Key, Count = g.Count(), Points = g.Sum(o => o.Points) }).ToListAsync(ct))
            .Select(d => new OperationDay(DateOnly.FromDateTime(d.Day), d.Count, d.Points)).OrderByDescending(d => d.Day).ToList();

        var top = await ops.GroupBy(o => o.UserId).Select(g => new { UserId = g.Key, Count = g.Count(), Points = g.Sum(o => o.Points) })
            .OrderByDescending(a => a.Points).Take(20).ToListAsync(ct);
        var ids = top.Select(a => a.UserId).ToList();
        var emails = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return new OperationsOverview(days, byKind.Sum(b => b.Count), byKind.Sum(b => b.Points), kinds, perDay,
            top.Select(a => new OperationAccount(a.UserId, emails.GetValueOrDefault(a.UserId), a.Count, a.Points)).ToList());
    }

    /// <summary>Changes a kind's weight. Null when it isn't valid (negative points, a unit without its size).</summary>
    public async Task<string?> SetWeightAsync(string code, int points, int? pointsPerUnit, int? unitSize, bool active, string admin,
        CancellationToken ct = default)
    {
        if (points < 0 || pointsPerUnit < 0 || (pointsPerUnit is not null && unitSize is not > 0))
            return "Points can't be negative, and points per unit need a unit size above 0.";
        await using var db = await factory.CreateDbContextAsync(ct);
        var kind = await db.OperationKinds.AsTracking().FirstOrDefaultAsync(k => k.Code == code, ct);
        if (kind is null) return $"No operation kind {code}.";
        var before = new { kind.Points, kind.PointsPerUnit, kind.UnitSize, kind.Active };
        kind.Points = points;
        kind.PointsPerUnit = pointsPerUnit;
        kind.UnitSize = pointsPerUnit is null ? null : unitSize;
        kind.Active = active;
        kind.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        db.AdminActions.Add(new AdminAction
        {
            Id = Guid.NewGuid(), AdminEmail = admin, Action = "operation_weight", Reason = "weights",
            Details = System.Text.Json.JsonSerializer.SerializeToDocument(new { code, before, after = new { points, pointsPerUnit, unitSize, active } }),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        return null;
    }
}
