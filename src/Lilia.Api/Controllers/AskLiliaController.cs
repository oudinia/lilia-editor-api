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

    public AskLiliaController(IAskLiliaRouter router) => _router = router;

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    public record AskLiliaRequest(string Message, string? Proficiency = null);
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
    public IActionResult Route([FromBody] AskLiliaRequest request)
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

    /// <summary>GET /api/ai/skills — the skill catalog (for an "Ask Lilia" picker / suggestions).</summary>
    [HttpGet("skills")]
    public IActionResult Skills() =>
        Ok(_router.Skills.Select(s => new { id = s.Id, name = s.Name, whenToUse = s.WhenToUse, tier = s.Tier }));
}
