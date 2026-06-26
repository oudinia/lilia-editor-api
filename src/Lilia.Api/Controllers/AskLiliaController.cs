using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// "Ask Lilia" — the single natural-language front door to the AI skill family.
///
/// This controller exposes the routing brain: given a message it classifies the
/// intent to a skill and returns the proficiency-aware system preamble the
/// generation step will use. Generation itself (running the chosen skill's full
/// guidance through the existing AI call path — governance, metering, model
/// catalog, verification) is wired next, per AiArchitectController. Until then,
/// the editor calls /ask/route to pick the skill, then the matching typed
/// endpoint (architect / improve-text / suggest-equation / citation-check …).
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AskLiliaController : ControllerBase
{
    private readonly IAskLiliaRouter _router;
    private readonly IAskLiliaService _ask;
    private readonly ILogger<AskLiliaController> _logger;

    public AskLiliaController(IAskLiliaRouter router, IAskLiliaService ask, ILogger<AskLiliaController> logger)
    {
        _router = router;
        _ask = ask;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    public record AskRouteRequest(string Message, string? Proficiency = null);
    public record AskRouteResponse(
        string SkillId, string SkillName, double Confidence, string Reason,
        string Proficiency, string Preamble);

    /// <summary>
    /// POST /api/ai/ask/route — classify a natural-language message to a skill and
    /// return the proficiency-aware system preamble. The router never fails: an
    /// unclear message routes to the document architect.
    /// </summary>
    [HttpPost("ask/route")]
    [ProducesResponseType(typeof(AskRouteResponse), StatusCodes.Status200OK)]
    public IActionResult Route([FromBody] AskRouteRequest request)
    {
        if (GetUserId() is null) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Message is required." });

        var route = _router.Route(request.Message);
        var level = ProficiencyGuidance.Parse(request.Proficiency);
        var skill = _router.Get(route.SkillId);
        var preamble = _router.BuildPreamble(skill, level);

        return Ok(new AskRouteResponse(
            route.SkillId, route.SkillName, route.Confidence, route.Reason,
            level.ToString().ToLowerInvariant(), preamble));
    }

    /// <summary>
    /// POST /api/ai/ask — the front door. Routes the message to a skill, runs it
    /// through the governed AI path (key gate, budget, model catalog, audit,
    /// metering), and returns { skillId, skillName, reply, usage, balance } — or 403
    /// { locked, reason, message } when gated. Text mode: the artifact (LML/BibTeX/
    /// LaTeX) is in the reply, in fenced blocks the editor can copy or apply.
    /// </summary>
    [HttpPost("ask")]
    [ProducesResponseType(typeof(AskLiliaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ask([FromBody] AskLiliaRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Message is required." });

        try
        {
            var outcome = await _ask.AskAsync(userId, request, ct);
            if (outcome.Locked)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { locked = true, reason = outcome.Reason, message = outcome.Message });
            return Ok(outcome.Response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AskLilia] Ask failed for user {UserId}", userId);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Ask Lilia failed to respond. Please try again." });
        }
    }

    /// <summary>GET /api/ai/skills — the skill catalog (for an "Ask Lilia" picker / suggestions).</summary>
    [HttpGet("skills")]
    public IActionResult Skills() =>
        Ok(_router.Skills.Select(s => new { id = s.Id, name = s.Name, whenToUse = s.WhenToUse, tier = s.Tier }));
}
