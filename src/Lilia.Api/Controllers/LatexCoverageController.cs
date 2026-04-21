using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Coverage telemetry for the LaTeX catalog. Read-only for now — Phase 3's
/// admin surface will mutate coverage_level through a separate endpoint.
/// Fleet-wide endpoint requires a Pro/Team/Admin plan (checked in the
/// handler once the entitlement middleware lands).
/// </summary>
[ApiController]
[Route("api/lilia/latex-coverage")]
[Authorize]
public class LatexCoverageController : ControllerBase
{
    private readonly ILatexCatalogService _catalog;
    private readonly ILogger<LatexCoverageController> _logger;

    public LatexCoverageController(ILatexCatalogService catalog, ILogger<LatexCoverageController> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// Fleet-wide coverage summary over a configurable window. Returns
    /// total tokens seen, counts by coverage_level, and the top 20
    /// unsupported tokens by hit count — the weekly triage feed.
    /// </summary>
    [HttpGet("report")]
    public async Task<ActionResult<CatalogCoverageReport>> GetReport(
        [FromQuery] int windowDays = 30,
        CancellationToken ct = default)
    {
        windowDays = Math.Clamp(windowDays, 1, 365);
        var report = await _catalog.GetCoverageReportAsync(TimeSpan.FromDays(windowDays), ct);
        return Ok(report);
    }

    /// <summary>
    /// Single-package coverage lookup — drives the Package Inspector
    /// modal. Returns null when the slug is unknown to the catalog
    /// (which means we've never encountered it, not that it's unsupported).
    /// </summary>
    [HttpGet("packages/{slug}")]
    public ActionResult<CatalogPackageEntry> GetPackage(string slug)
    {
        var entry = _catalog.LookupPackage(slug);
        if (entry is null) return NotFound();
        return Ok(entry);
    }
}
