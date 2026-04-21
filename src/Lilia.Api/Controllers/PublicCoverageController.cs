using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Public-facing LaTeX coverage surface — fuels lilia-editor.com/latex-
/// coverage (Batch F of the Q2 plan). Read-only, no auth, CDN-cacheable.
/// Exposes structural coverage + package metadata only; per-session
/// usage data stays private in <see cref="LatexCoverageController"/>.
/// </summary>
[ApiController]
[Route("api/public/latex-coverage")]
[AllowAnonymous]
public class PublicCoverageController : ControllerBase
{
    private readonly ILatexCatalogService _catalog;
    private readonly LiliaDbContext _db;

    public PublicCoverageController(ILatexCatalogService catalog, LiliaDbContext db)
    {
        _catalog = catalog;
        _db = db;
    }

    /// <summary>
    /// Top-level SLO badge + counters by coverage level + package counts
    /// by category. Cached by the CDN for 15 minutes — the catalog
    /// doesn't mutate often enough to warrant a shorter window.
    /// </summary>
    [HttpGet("summary")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicCoverageSummaryDto>> Summary(CancellationToken ct)
    {
        var tokens = await _db.LatexTokens
            .GroupBy(t => t.CoverageLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Tok(string level) => tokens.FirstOrDefault(x => x.Level == level)?.Count ?? 0;

        var packages = await _db.LatexPackages
            .GroupBy(p => p.CoverageLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Pkg(string level) => packages.FirstOrDefault(x => x.Level == level)?.Count ?? 0;

        // EF 10 can't translate GroupBy → record-constructor projections;
        // the earlier two GroupBy queries work because they project into
        // anonymous types. Mirror that pattern here and build the DTO in
        // memory post-materialisation.
        var categoryRaw = await _db.LatexPackages
            .GroupBy(p => p.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var categories = categoryRaw
            .OrderByDescending(x => x.Count)
            .Select(x => new CategoryBreakdownDto(x.Category, x.Count))
            .ToList();

        var totalCovered = Tok("full") + Tok("partial") + Tok("shimmed");
        var totalTokens = tokens.Sum(x => x.Count);
        var sloPercent = totalTokens == 0 ? 0 : Math.Round(100.0 * totalCovered / totalTokens, 1);

        var classCount = await _db.LatexDocumentClasses.CountAsync(ct);

        return Ok(new PublicCoverageSummaryDto(
            TotalTokens: totalTokens,
            TotalPackages: packages.Sum(x => x.Count),
            TotalClasses: classCount,
            CoveragePercent: sloPercent,
            Tokens: new CoverageCountsDto(Tok("full"), Tok("partial"), Tok("shimmed"), Tok("none"), Tok("unsupported")),
            Packages: new CoverageCountsDto(Pkg("full"), Pkg("partial"), Pkg("shimmed"), Pkg("none"), Pkg("unsupported")),
            Categories: categories));
    }

    /// <summary>
    /// Searchable + filterable package list. No pagination yet — catalog
    /// is &lt;100 packages at launch.
    /// </summary>
    // VaryByQueryKeys requires AddResponseCaching + UseResponseCaching, which
    // we don't register — the public CDN keys on URL + query automatically,
    // so the attribute would only buy in-process caching we don't want.
    [HttpGet("packages")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<List<PublicPackageDto>>> Packages(
        [FromQuery] string? category = null,
        [FromQuery] string? coverageLevel = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        var query = _db.LatexPackages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);
        if (!string.IsNullOrWhiteSpace(coverageLevel)) query = query.Where(p => p.CoverageLevel == coverageLevel);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Slug, needle) || EF.Functions.ILike(p.DisplayName, needle));
        }

        var rows = await query
            .OrderBy(p => p.Category).ThenBy(p => p.Slug)
            .Select(p => new PublicPackageDto(
                p.Slug, p.DisplayName, p.Category, p.CoverageLevel, p.CoverageNotes, p.CtanUrl))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Per-package detail — metadata + the list of tokens it ships.
    /// Drives the detail panel on the public coverage page.
    /// </summary>
    [HttpGet("packages/{slug}")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicPackageDetailDto>> PackageDetail(string slug, CancellationToken ct)
    {
        var pkg = await _db.LatexPackages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (pkg is null) return NotFound();

        var tokens = await _db.LatexTokens.AsNoTracking()
            .Where(t => t.PackageSlug == slug)
            .OrderBy(t => t.Kind).ThenBy(t => t.Name)
            .Select(t => new PublicTokenDto(
                t.Name, t.Kind, t.CoverageLevel, t.MapsToBlockType, t.SemanticCategory, t.Notes))
            .ToListAsync(ct);

        return Ok(new PublicPackageDetailDto(
            Slug: pkg.Slug,
            DisplayName: pkg.DisplayName,
            Category: pkg.Category,
            CoverageLevel: pkg.CoverageLevel,
            CoverageNotes: pkg.CoverageNotes,
            CtanUrl: pkg.CtanUrl,
            Tokens: tokens));
    }

    /// <summary>
    /// Document classes — articled / beamer / moderncv etc. Small list,
    /// returned ungated so the coverage page can render a "classes we
    /// support" section.
    /// </summary>
    [HttpGet("classes")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<List<PublicClassDto>>> Classes(CancellationToken ct)
    {
        var rows = await _db.LatexDocumentClasses.AsNoTracking()
            .OrderBy(c => c.Category).ThenBy(c => c.Slug)
            .Select(c => new PublicClassDto(
                c.Slug, c.DisplayName, c.Category, c.CoverageLevel, c.DefaultEngine, c.Notes))
            .ToListAsync(ct);
        return Ok(rows);
    }
}

public sealed record PublicCoverageSummaryDto(
    int TotalTokens,
    int TotalPackages,
    int TotalClasses,
    double CoveragePercent,
    CoverageCountsDto Tokens,
    CoverageCountsDto Packages,
    List<CategoryBreakdownDto> Categories);

public sealed record CoverageCountsDto(int Full, int Partial, int Shimmed, int None, int Unsupported);

public sealed record CategoryBreakdownDto(string Category, int Count);

public sealed record PublicPackageDto(string Slug, string DisplayName, string Category, string CoverageLevel, string? Notes, string? CtanUrl);

public sealed record PublicPackageDetailDto(
    string Slug,
    string DisplayName,
    string Category,
    string CoverageLevel,
    string? CoverageNotes,
    string? CtanUrl,
    List<PublicTokenDto> Tokens);

public sealed record PublicTokenDto(string Name, string Kind, string CoverageLevel, string? MapsToBlockType, string? SemanticCategory, string? Notes);

public sealed record PublicClassDto(string Slug, string DisplayName, string Category, string CoverageLevel, string? DefaultEngine, string? Notes);
