using System.Text.Json;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly TimeSpan _keepAliveAfter;
    private readonly TimeSpan _keepAliveEvery;

    public AskLiliaController(IAskLiliaRouter router, IAskLiliaService ask, ILogger<AskLiliaController> logger,
        IConfiguration? configuration = null)
    {
        _router = router;
        _ask = ask;
        _logger = logger;
        _keepAliveAfter = TimeSpan.FromSeconds(configuration?.GetValue("AI:AskKeepAliveAfterSeconds", 20d) ?? 20d);
        _keepAliveEvery = TimeSpan.FromSeconds(configuration?.GetValue("AI:AskKeepAliveEverySeconds", 10d) ?? 10d);
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
        // A photo or a paste of maths needs no words: "Read the maths" is the message (18 Oct).
        if (request is null || (string.IsNullOrWhiteSpace(request.Message) && request.Math is null))
            return BadRequest(new { message = "Message is required." });
        if (string.IsNullOrWhiteSpace(request.Message)) request = request with { Message = "" };

        var work = _ask.AskAsync(userId, request, ct);
        if (await Task.WhenAny(work, Task.Delay(_keepAliveAfter, CancellationToken.None)) == work)
        {
            var (status, body) = await OutcomeAsync(work, userId);
            return StatusCode(status, body);
        }

        // A long answer (10 Nov 2026): Cloudflare answers 524 when the server has sent nothing for about 125 s,
        // and a chapter draft can take longer, so the browser saw an error while the draft was still landing
        // in the paper. Start the answer now and send a space every few seconds (JSON allows leading
        // whitespace); the outcome follows as JSON, and a lock or failure that comes late says its status in it.
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        try
        {
            await Response.StartAsync(ct);
            while (!work.IsCompleted)
            {
                await Response.Body.WriteAsync(" "u8.ToArray(), ct);
                await Response.Body.FlushAsync(ct);
                await Task.WhenAny(work, Task.Delay(_keepAliveEvery, CancellationToken.None));
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // The browser left; the turn ends on the same token and records itself.
        }
        var (lateStatus, lateBody) = await OutcomeAsync(work, userId);
        if (ct.IsCancellationRequested) return new EmptyResult();
        object payload = lateStatus == StatusCodes.Status200OK ? lateBody : Merge(lateBody, lateStatus);
        var json = HttpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.JsonSerializerOptions;
        await JsonSerializer.SerializeAsync(Response.Body, payload, payload.GetType(), json, CancellationToken.None);
        return new EmptyResult();
    }

    /// <summary>The status and body for a finished turn, as the endpoint has always answered them.</summary>
    private async Task<(int Status, object Body)> OutcomeAsync(Task<AskLiliaResult> work, string userId)
    {
        try
        {
            var outcome = await work;
            if (outcome.Locked)
                return (StatusCodes.Status403Forbidden, new { locked = true, reason = outcome.Reason, message = outcome.Message });
            return (StatusCodes.Status200OK, outcome.Response!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AskLilia] Ask failed for user {UserId}", userId);
            return (StatusCodes.Status502BadGateway, new { message = "Ask Lilia failed to respond. Please try again." });
        }
    }

    /// <summary>A late lock or failure inside the 200 already sent: its body with the status it would have had.</summary>
    private static Dictionary<string, object?> Merge(object body, int status)
    {
        var map = JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        map["status"] = status;
        return map;
    }

    /// <summary>GET /api/ai/skills — the skill catalog (for an "Ask Lilia" picker / suggestions).</summary>
    [HttpGet("skills")]
    public IActionResult Skills() =>
        Ok(_router.Skills.Select(s => new { id = s.Id, name = s.Name, whenToUse = s.WhenToUse, tier = s.Tier }));

    /// <summary>
    /// GET /api/ai/skills/{id}/download — a skill's SKILL.md, to run with the author's own AI (10 Nov 2026: the
    /// app offered an old structure-only file whose JSON went into a paste box "New from structure" no longer has;
    /// this is the skill Ask Lilia itself runs, which writes LML for Import → Paste). Only the router's skills.
    /// </summary>
    [HttpGet("skills/{id}/download")]
    public IActionResult DownloadSkill(string id)
    {
        if (!_router.Skills.Any(s => s.Id == id) || AiSkillGuidance.File(id) is not { } md) return NotFound();
        return File(System.Text.Encoding.UTF8.GetBytes(md), "text/markdown; charset=utf-8", $"{id}.SKILL.md");
    }
}
