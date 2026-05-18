using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

// =====================================================================
//  E2E scenario API.
//
//  Endpoints split into three groups:
//    GET  /api/e2e/modules / surfaces / scenarios / block-types
//         — admin reads, authenticated.
//    POST /api/e2e/runs / results / runs/{id}/finalize
//         — reporter writes from Playwright; allow-anonymous since the
//         reporter runs in CI without a Stytch session. Locked down by
//         IP allowlist or shared secret if exposed beyond dev.
//    POST /api/e2e/coverage-events
//         — bulk UI interaction events from the editor (test or
//         sampled real-user); allow-anonymous, server validates the
//         event payload shape.
// =====================================================================

[ApiController]
[Route("api/e2e")]
public class E2EController : ControllerBase
{
    private readonly IE2EService _svc;
    private readonly ILogger<E2EController> _logger;

    public E2EController(IE2EService svc, ILogger<E2EController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    // ---- Catalogue reads (authenticated admin) ----

    [HttpGet("modules")]
    [Authorize]
    public async Task<ActionResult<List<E2EModuleDto>>> ListModules(CancellationToken ct)
        => Ok(await _svc.ListModulesAsync(ct));

    [HttpGet("surfaces")]
    [Authorize]
    public async Task<ActionResult<List<E2ESurfaceDto>>> ListSurfaces(
        [FromQuery] string? module, CancellationToken ct)
        => Ok(await _svc.ListSurfacesAsync(module, ct));

    [HttpGet("block-types")]
    [AllowAnonymous]
    public async Task<ActionResult<List<E2EBlockTypeDto>>> ListBlockTypes(CancellationToken ct)
        => Ok(await _svc.ListBlockTypesAsync(ct));

    // ---- Scenarios ----

    [HttpGet("scenarios")]
    [Authorize]
    public async Task<ActionResult<List<E2EScenarioListItemDto>>> ListScenarios(
        [FromQuery] string? module,
        [FromQuery] string? surface,
        [FromQuery] string? level,
        [FromQuery] string? review_state,
        [FromQuery] string? criticality,
        CancellationToken ct)
        => Ok(await _svc.ListScenariosAsync(module, surface, level, review_state, criticality, ct));

    [HttpGet("scenarios/{slug}")]
    [Authorize]
    public async Task<ActionResult<E2EScenarioDetailDto>> GetScenario(
        string slug, CancellationToken ct)
    {
        var result = await _svc.GetScenarioAsync(slug, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // ---- Reporter ingest (allow-anonymous) ----
    //
    // The Playwright reporter runs in CI / local dev without a Stytch
    // session. We allow-anonymous on these endpoints and treat them as
    // append-only ingest. Mitigations: payload size limits, scenario
    // resolution drops unknown automation_content silently, and no
    // sensitive data is exposed by these routes.

    [HttpPost("runs")]
    [AllowAnonymous]
    public async Task<ActionResult<StartRunResponse>> StartRun(
        [FromBody] StartRunRequest req, CancellationToken ct)
        => Ok(await _svc.StartRunAsync(req, ct));

    [HttpPost("results")]
    [AllowAnonymous]
    public async Task<IActionResult> RecordResult(
        [FromBody] RecordResultRequest req, CancellationToken ct)
    {
        var id = await _svc.RecordResultAsync(req, ct);
        return id is null
            ? Ok(new { recorded = false, reason = "scenario_not_found" })
            : Ok(new { recorded = true, id });
    }

    [HttpPost("runs/{id:guid}/finalize")]
    [AllowAnonymous]
    public async Task<IActionResult> FinalizeRun(
        Guid id, [FromBody] FinalizeRunRequest req, CancellationToken ct)
    {
        await _svc.FinalizeRunAsync(id, req, ct);
        return Ok();
    }

    // ---- Coverage events (allow-anonymous, bulk) ----

    [HttpPost("coverage-events")]
    [AllowAnonymous]
    public async Task<IActionResult> CoverageEvents(
        [FromBody] CoverageEventsRequest req, CancellationToken ct)
    {
        // Cap per-request payload to prevent abuse if the prod-sampled
        // emitter ever misbehaves. 500 events = ~5min of one user's
        // interactions at default rate.
        if (req.Events.Count > 500)
        {
            return BadRequest(new { error = "too_many_events", max = 500 });
        }
        var count = await _svc.IngestCoverageEventsAsync(req, ct);
        return Ok(new { ingested = count });
    }

    // ---- Coverage reads ----

    [HttpGet("coverage/elements")]
    [Authorize]
    public async Task<ActionResult<List<UIElementCoverageDto>>> ElementCoverage(
        [FromQuery] string? module, CancellationToken ct)
        => Ok(await _svc.GetUIElementCoverageAsync(module, ct));
}
