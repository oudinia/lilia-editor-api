using System.Collections.Concurrent;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lilia.Api.Services;

/// <summary>
/// See ILatexCatalogService. Boots a concurrent dictionary cache off
/// latex_tokens + latex_packages + latex_document_classes, then serves
/// every lookup synchronously in-process. Writes (ReportUnknownAsync,
/// RecordUsageAsync) hit the DB directly and also patch the cache so
/// the next lookup in the same process sees the new row.
/// </summary>
public class LatexCatalogService : ILatexCatalogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LatexCatalogService> _logger;

    // Lookup keyed by (name, kind, packageSlug ?? "").
    private readonly ConcurrentDictionary<(string Name, string Kind, string Package), CatalogTokenEntry> _tokens = new();
    private readonly ConcurrentDictionary<string, CatalogPackageEntry> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CatalogDocumentClassEntry> _classes = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LatexCatalogService(IServiceScopeFactory scopeFactory, ILogger<LatexCatalogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Fire-and-forget preload called from Program.cs at boot so the first
    // import doesn't pay the warmup cost.
    public async Task PreloadAsync()
    {
        await EnsureLoadedAsync(CancellationToken.None);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded) return;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

            var tokens = await db.LatexTokens.AsNoTracking().ToListAsync(ct);
            foreach (var t in tokens)
            {
                var entry = new CatalogTokenEntry(t.Id, t.Name, t.Kind, t.PackageSlug,
                    t.Arity, t.OptionalArity, t.ExpectsBody, t.SemanticCategory,
                    t.MapsToBlockType, t.CoverageLevel, t.AliasOf, t.HandlerKind);
                _tokens[(t.Name, t.Kind, t.PackageSlug ?? string.Empty)] = entry;
            }

            var packages = await db.LatexPackages.AsNoTracking().ToListAsync(ct);
            foreach (var p in packages)
            {
                _packages[p.Slug] = new CatalogPackageEntry(p.Slug, p.DisplayName,
                    p.Category, p.CoverageLevel, p.CoverageNotes, p.CtanUrl);
            }

            var classes = await db.LatexDocumentClasses.AsNoTracking().ToListAsync(ct);
            foreach (var c in classes)
            {
                _classes[c.Slug] = new CatalogDocumentClassEntry(c.Slug, c.DisplayName,
                    c.Category, c.CoverageLevel, c.DefaultEngine, c.ShimName);
            }

            _loaded = true;
            _logger.LogInformation("[LatexCatalog] Loaded {TokenCount} tokens, {PackageCount} packages, {ClassCount} classes",
                _tokens.Count, _packages.Count, _classes.Count);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public CatalogTokenEntry? LookupToken(string name, string kind, string? packageSlug = null)
    {
        // Fast path — assume preloaded. If not, caller will fall through
        // to ReportUnknownAsync which awaits EnsureLoaded as a safety net.
        if (!_loaded) return null;

        // 1) Direct hit on the requested scope.
        if (_tokens.TryGetValue((name, kind, packageSlug ?? string.Empty), out var direct)) return direct;

        // 2) If the caller named a package, fall back to kernel.
        if (packageSlug != null && _tokens.TryGetValue((name, kind, string.Empty), out var kernel)) return kernel;

        // 3) If the caller didn't name a package and the kernel scope
        //    has no row (or only a stale `unsupported` one that was
        //    deleted), pick the best package-scoped row that shares
        //    (name, kind). Prefer higher coverage (full > partial >
        //    shimmed > none > unsupported). This keeps a scanner-style
        //    caller — parser or diag — honest about coverage even
        //    when a token is only catalogued under a specific package
        //    (e.g. \theorem is at amsthm scope, not kernel).
        if (packageSlug == null)
        {
            CatalogTokenEntry? best = null;
            var bestRank = int.MaxValue;
            foreach (var kv in _tokens)
            {
                if (kv.Key.Name == name && kv.Key.Kind == kind)
                {
                    var rank = kv.Value.CoverageLevel switch
                    {
                        "full" => 0,
                        "partial" => 1,
                        "shimmed" => 2,
                        "none" => 3,
                        _ => 4, // unsupported
                    };
                    if (rank < bestRank)
                    {
                        best = kv.Value;
                        bestRank = rank;
                    }
                }
            }
            return best;
        }

        return null;
    }

    public CatalogPackageEntry? LookupPackage(string slug) =>
        _loaded && _packages.TryGetValue(slug, out var e) ? e : null;

    public CatalogDocumentClassEntry? LookupDocumentClass(string slug) =>
        _loaded && _classes.TryGetValue(slug, out var e) ? e : null;

    public async Task<Guid> ReportUnknownAsync(string name, string kind, string? packageSlug, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        // Already known? Don't re-insert — just return the cached id.
        if (_tokens.TryGetValue((name, kind, packageSlug ?? string.Empty), out var existing))
        {
            return existing.Id;
        }

        // Raw ADO on a dedicated pooled connection — EF Core 10 refuses to
        // compose SqlQueryRaw + FirstAsync against an INSERT ... RETURNING
        // statement ("non-composable SQL"), and a fresh connection also
        // keeps us off EF's scoped connection (same rationale as the rest
        // of the catalog code path).
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var connStr = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No connection string configured for LiliaDbContext");

        const string sql = @"
INSERT INTO latex_tokens (id, name, kind, package_slug, coverage_level, notes, created_at, updated_at)
VALUES (gen_random_uuid(), @name, @kind, @pkg, 'unsupported',
        'Auto-inserted by parser — not yet cataloged. Review and upgrade coverage_level when implementing handling.',
        NOW(), NOW())
ON CONFLICT (name, kind, package_slug) DO UPDATE SET updated_at = NOW()
RETURNING id;";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("pkg", (object?)packageSlug ?? DBNull.Value);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        if (scalar is null || scalar is DBNull)
            throw new InvalidOperationException($"INSERT ... RETURNING produced no row for ({kind} '{name}').");
        var idResult = (Guid)scalar;

        // Patch cache so the next parse in this process skips the DB round trip.
        var entry = new CatalogTokenEntry(idResult, name, kind, packageSlug,
            null, null, false, null, null, "unsupported", null);
        _tokens[(name, kind, packageSlug ?? string.Empty)] = entry;

        _logger.LogInformation("[LatexCatalog] Auto-inserted unknown token: {Kind} '{Name}' (package={Pkg})",
            kind, name, packageSlug ?? "kernel");

        return idResult;
    }

    public async Task RecordUsageAsync(Guid sessionId, IEnumerable<CatalogTokenUsage> tokens, CancellationToken ct = default)
    {
        var list = tokens.ToList();
        if (list.Count == 0) return;

        _logger.LogInformation("[LatexCatalog] RecordUsageAsync start session={Session} usages={Count}", sessionId, list.Count);

        // One bulk-upsert — build a multi-row INSERT with ON CONFLICT
        // incrementing the count. DB-first: rows never transit .NET.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

        var values = string.Join(",", list.Select((_, i) => $"(gen_random_uuid(), @t{i}, @s, @c{i}, NOW(), NOW())"));
        var sql = $@"
INSERT INTO latex_token_usage (id, token_id, session_id, count, first_seen_at, last_seen_at)
VALUES {values}
ON CONFLICT (token_id, session_id) DO UPDATE
  SET count = latex_token_usage.count + EXCLUDED.count,
      last_seen_at = NOW();";

        var parameters = new List<NpgsqlParameter> { new("s", sessionId) };
        for (var i = 0; i < list.Count; i++)
        {
            parameters.Add(new NpgsqlParameter($"t{i}", list[i].TokenId));
            parameters.Add(new NpgsqlParameter($"c{i}", list[i].Count));
        }
        var rows = await db.Database.ExecuteSqlRawAsync(sql, parameters.Cast<object>().ToArray());
        _logger.LogInformation("[LatexCatalog] RecordUsageAsync done session={Session} sqlRows={Rows}", sessionId, rows);
    }

    public async Task<CatalogCoverageReport> GetCoverageReportAsync(TimeSpan window, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

        var since = DateTime.UtcNow - window;

        // One query: join usage rows in the window with their tokens, group
        // by coverage_level. Counts computed in SQL, not in .NET.
        var summary = await db.LatexTokenUsages
            .Where(u => u.LastSeenAt >= since)
            .Join(db.LatexTokens,
                  u => u.TokenId,
                  t => t.Id,
                  (u, t) => new { t.CoverageLevel, u.Count, t.Name, t.Kind, t.PackageSlug })
            .GroupBy(x => x.CoverageLevel)
            .Select(g => new { CoverageLevel = g.Key, Total = g.Sum(x => x.Count) })
            .ToListAsync(ct);

        int Get(string level) => summary.FirstOrDefault(s => s.CoverageLevel == level)?.Total ?? 0;

        var topUnsupported = await db.LatexTokenUsages
            .Where(u => u.LastSeenAt >= since)
            .Join(db.LatexTokens.Where(t => t.CoverageLevel == "unsupported" || t.CoverageLevel == "none"),
                  u => u.TokenId,
                  t => t.Id,
                  (u, t) => new { t.Name, t.Kind, t.PackageSlug, u.Count })
            .GroupBy(x => new { x.Name, x.Kind, x.PackageSlug })
            .Select(g => new { g.Key.Name, g.Key.Kind, g.Key.PackageSlug, Count = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToListAsync(ct);

        return new CatalogCoverageReport(
            TotalTokensSeen: summary.Sum(s => s.Total),
            FullCount: Get("full"),
            PartialCount: Get("partial"),
            ShimmedCount: Get("shimmed"),
            NoneCount: Get("none"),
            UnsupportedCount: Get("unsupported"),
            TopUnsupported: topUnsupported.Select(x => (x.Name, x.Kind, x.PackageSlug, x.Count)).ToList());
    }

    public async Task<SessionCoverage> GetSessionCoverageAsync(Guid sessionId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

        // One join — usage rows + token metadata + optional package
        // display name, sorted so the UI gets the sharpest stuff first.
        var rows = await db.LatexTokenUsages
            .Where(u => u.SessionId == sessionId)
            .Join(db.LatexTokens,
                  u => u.TokenId,
                  t => t.Id,
                  (u, t) => new { u.Count, t.Name, t.Kind, t.PackageSlug, t.CoverageLevel, t.MapsToBlockType, t.SemanticCategory, t.Notes })
            .GroupJoin(db.LatexPackages,
                       x => x.PackageSlug,
                       p => p.Slug,
                       (x, pkgs) => new { x, pkg = pkgs.FirstOrDefault() })
            .Select(r => new SessionCoverageRow(
                r.x.Name,
                r.x.Kind,
                r.x.PackageSlug,
                r.pkg != null ? r.pkg.DisplayName : null,
                r.x.CoverageLevel,
                r.x.MapsToBlockType,
                r.x.SemanticCategory,
                r.x.Count,
                r.x.Notes))
            .ToListAsync(ct);

        // Sort in-memory — order: unsupported > none > partial > shimmed
        // > full, then by count desc. Worst stuff first for the user.
        var rank = new Dictionary<string, int>
        {
            ["unsupported"] = 0,
            ["none"] = 1,
            ["partial"] = 2,
            ["shimmed"] = 3,
            ["full"] = 4,
        };
        var ordered = rows
            .OrderBy(r => rank.TryGetValue(r.CoverageLevel, out var v) ? v : 99)
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.Name)
            .ToList();

        int By(string level) => rows.Where(r => r.CoverageLevel == level).Sum(r => r.Count);

        return new SessionCoverage(
            TotalTokens: rows.Sum(r => r.Count),
            DistinctTokens: rows.Count,
            FullCount: By("full"),
            PartialCount: By("partial"),
            ShimmedCount: By("shimmed"),
            NoneCount: By("none"),
            UnsupportedCount: By("unsupported"),
            Tokens: ordered);
    }
}
