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

    public TeamsController(ITeamService teamService)
    {
        _teamService = teamService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

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
