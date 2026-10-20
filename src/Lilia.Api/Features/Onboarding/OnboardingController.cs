using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Features.Onboarding;

/// <summary>
/// The person's own onboarding steps (reports/2026-10-21-welcome-onboarding.md). The web sends its
/// interface language in <c>X-Lilia-Locale</c>, which keeps the emails in that language.
/// </summary>
[ApiController]
[Route("api/me/onboarding")]
[Authorize]
public class OnboardingController(IOnboardingService onboarding) : ControllerBase
{
    private string? UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<OnboardingDto>> Get(CancellationToken ct)
    {
        if (UserId is not { } id) return Unauthorized();
        await onboarding.SetLocaleAsync(id, Request.Headers["X-Lilia-Locale"].FirstOrDefault(), ct);
        return await onboarding.GetAsync(id, ct) is { } dto ? Ok(dto) : NotFound();
    }

    /// <summary>Records one step: start, verified, first-document, tour-step, tour-seen, tour-restart,
    /// first-validate-pass, first-export, tips-opt-out. Each is idempotent.</summary>
    [HttpPost("{step}")]
    public async Task<ActionResult<OnboardingDto>> Record(string step, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] OnboardingStepRequest? request, CancellationToken ct)
    {
        if (UserId is not { } id) return Unauthorized();
        if (!OnboardingSteps.All.Contains(step)) return BadRequest(new { error = "unknown_step", steps = OnboardingSteps.All });
        await onboarding.SetLocaleAsync(id, Request.Headers["X-Lilia-Locale"].FirstOrDefault(), ct);
        return await onboarding.RecordAsync(id, step, request, ct) is { } dto ? Ok(dto) : NotFound();
    }
}
