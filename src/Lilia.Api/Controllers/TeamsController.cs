using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly ITeamService _teamService;
    private readonly IAuditService _auditService;
    private readonly ITeamCodenameGenerator _codenameGenerator;
    private readonly IEmailService _email;

    public TeamsController(
        ITeamService teamService,
        IAuditService auditService,
        ITeamCodenameGenerator codenameGenerator,
        IEmailService email)
    {
        _teamService = teamService;
        _auditService = auditService;
        _codenameGenerator = codenameGenerator;
        _email = email;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Generate research-lab-style codename suggestions. Used by the
    /// editor's team-create form: user clicks "Generate" → picks one
    /// or types their own. Defaults to 1 suggestion; pass ?count=5 to
    /// get a small picker.
    /// </summary>
    [HttpGet("suggest-codename")]
    [AllowAnonymous]
    public ActionResult<object> SuggestCodename([FromQuery] int count = 1)
    {
        if (count <= 1)
        {
            return Ok(new { codename = _codenameGenerator.Generate() });
        }
        return Ok(new { codenames = _codenameGenerator.Suggest(count) });
    }

    /// <summary>
    /// Ad-hoc test for the team-welcome email template. Sends a one-shot
    /// email to the given address with a freshly-generated codename — no
    /// team is created in the DB. Validates Resend config + template.
    ///
    /// Gated by the `Internal:TestEmailKey` config (env var
    /// <c>Internal__TestEmailKey</c>). Header `X-Lilia-Test-Key` must
    /// match. Cookie-auth is unreliable on cross-origin scripted calls,
    /// so we use a shared secret for this internal-tooling endpoint.
    /// </summary>
    [HttpPost("test-welcome-email")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> TestWelcomeEmail(
        [FromBody] TestWelcomeEmailRequest req,
        [FromServices] IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(req.Email)) return BadRequest(new { error = "email required" });
        var expected = config["Internal:TestEmailKey"];
        var provided = Request.Headers["X-Lilia-Test-Key"].ToString();
        if (string.IsNullOrEmpty(expected) || provided != expected)
        {
            return Unauthorized(new { error = "missing or invalid X-Lilia-Test-Key" });
        }
        var codename = req.Codename ?? _codenameGenerator.Generate();
        try
        {
            await _email.SendTeamWelcomeAsync(req.Email, req.FirstName, codename);
            return Ok(new { sent = true, to = req.Email, codename });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { sent = false, to = req.Email, codename, error = ex.Message });
        }
    }

    public record TestWelcomeEmailRequest(string Email, string? FirstName, string? Codename);

    [HttpGet]
    public async Task<ActionResult<List<TeamDto>>> GetTeams()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var teams = await _teamService.GetTeamsAsync(userId);
        return Ok(teams);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TeamDetailsDto>> GetTeam(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var team = await _teamService.GetTeamAsync(id, userId);
        if (team == null) return NotFound();
        return Ok(team);
    }

    [HttpPost]
    public async Task<ActionResult<TeamDto>> CreateTeam([FromBody] CreateTeamDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var team = await _teamService.CreateTeamAsync(userId, dto);
        await _auditService.LogAsync("team.create", "Team", team.Id.ToString(), new { dto.Name });
        return CreatedAtAction(nameof(GetTeam), new { id = team.Id }, team);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TeamDto>> UpdateTeam(Guid id, [FromBody] UpdateTeamDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var team = await _teamService.UpdateTeamAsync(id, userId, dto);
        if (team == null) return NotFound();
        return Ok(team);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteTeam(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _teamService.DeleteTeamAsync(id, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<TeamMemberDto>>> GetTeamMembers(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var members = await _teamService.GetTeamMembersAsync(id, userId);
        return Ok(members);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<TeamMemberDto>> InviteMember(Guid id, [FromBody] InviteTeamMemberDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var member = await _teamService.InviteMemberAsync(id, userId, dto);
        if (member == null) return NotFound();
        await _auditService.LogAsync("team.member.add", "Team", id.ToString(), new { dto.Email, dto.Role });
        return Ok(member);
    }

    /// <summary>
    /// Add a member by user-id (privacy-safe path — no email). Pairs with
    /// the /api/users/search endpoint which resolves a picked user to
    /// their id. Fires an in-app notification, no email send.
    /// </summary>
    [HttpPost("{id:guid}/members/addById")]
    public async Task<ActionResult<TeamMemberDto>> AddMemberByUserId(Guid id, [FromBody] AddTeamMemberByUserIdDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var member = await _teamService.AddMemberByUserIdAsync(id, userId, dto);
        if (member == null) return NotFound();
        await _auditService.LogAsync("team.member.addById", "Team", id.ToString(), new { dto.UserId, dto.Role });
        return Ok(member);
    }

    [HttpPut("{id:guid}/members/{targetUserId}")]
    public async Task<ActionResult<TeamMemberDto>> UpdateMemberRole(Guid id, string targetUserId, [FromBody] UpdateTeamMemberRoleDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var member = await _teamService.UpdateMemberRoleAsync(id, targetUserId, userId, dto);
        if (member == null) return NotFound();
        return Ok(member);
    }

    [HttpDelete("{id:guid}/members/{targetUserId}")]
    public async Task<ActionResult> RemoveMember(Guid id, string targetUserId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await _teamService.RemoveMemberAsync(id, targetUserId, userId);
        if (!result) return NotFound();
        await _auditService.LogAsync("team.member.remove", "Team", id.ToString(), new { targetUserId });
        return NoContent();
    }

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<List<DocumentListDto>>> GetTeamDocuments(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var documents = await _teamService.GetTeamDocumentsAsync(id, userId);
        return Ok(documents);
    }
}
