using System.Collections.Concurrent;
using Lilia.Engines;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Tools.Api.Services;

/// <summary>
/// Engine requirements read from <c>latex_packages.requires_engine</c>, held in
/// memory.
///
/// <para>Loaded once at boot and served synchronously, the same shape as the tool
/// registry and the model catalog: this sits on the path of every compile, so it
/// must never become a database round trip.</para>
///
/// <para>A failed load is not fatal. The resolver still has the regex floor
/// underneath it and the tools runner still retries on a mismatch, so the cost of
/// an empty catalog is a wasted compile now and then — not a wrong verdict.</para>
/// </summary>
public sealed class PackageEngineRequirements : IEngineRequirementSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PackageEngineRequirements> _logger;
    private readonly ConcurrentDictionary<string, LatexEngine> _bySlug =
        new(StringComparer.OrdinalIgnoreCase);

    public PackageEngineRequirements(IServiceScopeFactory scopeFactory, ILogger<PackageEngineRequirements> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PreloadAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

            var rows = await db.LatexPackages
                .AsNoTracking()
                .Where(p => p.RequiresEngine != null)
                .Select(p => new { p.Slug, p.RequiresEngine })
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                // ParseEngine maps anything unrecognised to pdflatex, which as a
                // *requirement* means "no requirement" — so only keep escalations.
                var engine = row.RequiresEngine.ParseEngine();
                if (engine != LatexEngine.Pdflatex) _bySlug[row.Slug] = engine;
            }

            _logger.LogInformation("[Engines] {Count} package engine requirements loaded", _bySlug.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Engines] could not load package engine requirements; falling back to detection alone");
        }
    }

    public LatexEngine? RequiredEngine(string packageSlug) =>
        _bySlug.TryGetValue(packageSlug, out var engine) ? engine : null;
}
