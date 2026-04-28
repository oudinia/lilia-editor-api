using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Internal admin surface for the Typst translation coverage report.
/// Mirrors the LaTeX coverage controller pattern but stays
/// authentication-required (NOT under /api/public) until post-launch.
/// Renders a single JSON payload combining the catalog and the last-N
/// silent_fallback hits — the loop the design doc described.
/// </summary>
[ApiController]
[Route("api/admin/typst-coverage")]
[Authorize]
public class TypstCoverageAdminController : ControllerBase
{
    private readonly ITypstCoverageService _coverage;
    private readonly ILogger<TypstCoverageAdminController> _logger;

    public TypstCoverageAdminController(
        ITypstCoverageService coverage,
        ILogger<TypstCoverageAdminController> logger)
    {
        _coverage = coverage;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/typst-coverage?windowHours=168 (default 7 days).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? windowHours, CancellationToken ct)
    {
        var window = windowHours.HasValue && windowHours.Value > 0
            ? TimeSpan.FromHours(windowHours.Value)
            : TimeSpan.FromDays(7);

        var report = await _coverage.GetReportAsync(window, ct);

        return Ok(new
        {
            summary = new
            {
                handlerCount = report.HandlerCount,
                handlersActive = report.HandlersActive,
                gapCount = report.GapCount,
                gapsOpen = report.GapsOpen,
                gapsScheduled = report.GapsScheduled,
                gapsShipped = report.GapsShipped,
                fallbackEventsInWindow = report.FallbackEventsLastWindow,
                eventWindowHours = report.EventWindow.TotalHours,
            },
            byCategory = report.ByCategory,
            topFallbackTokens = report.TopFallbackTokens,
            handlers = report.Handlers.Select(h => new
            {
                h.HandlerKey,
                h.Category,
                h.SourcePattern,
                h.TypstEmit,
                h.Status,
                shippedAt = h.ShippedAt,
                h.ShippedIn,
                h.Notes,
            }),
            gaps = report.Gaps.Select(g => new
            {
                g.GapKey,
                g.Category,
                g.SamplePattern,
                g.TypstErrorShape,
                g.MitigationStatus,
                g.BlockingSeverity,
                g.Notes,
            }),
        });
    }
}
