using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Owner-scoped aggregation behind the editor's Sharing surface (/shared).
/// "Shared with me" is served by the document list (role != owner); the
/// endpoints here back the "Shared by me" and "People" tabs, which need a
/// cross-document view the per-document collaborators API can't give.
/// Phase B — see lilia-docs/design-handoffs/2026-07-01-sharing-settings-*.md.
/// </summary>
[ApiController]
[Route("api/shared")]
[Authorize]
public class SharingController : ControllerBase
{
    private readonly ISharingService _sharingService;

    public SharingController(ISharingService sharingService)
    {
        _sharingService = sharingService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet("by-me")]
    public async Task<ActionResult<List<SharedByMeDto>>> GetSharedByMe()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var items = await _sharingService.GetSharedByMeAsync(userId);
        return Ok(items);
    }

    [HttpGet("people")]
    public async Task<ActionResult<List<SharedPersonDto>>> GetPeople()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var people = await _sharingService.GetPeopleAsync(userId);
        return Ok(people);
    }

    [HttpPost("resend")]
    public async Task<ActionResult<InviteResultDto>> ResendInvite([FromBody] ResendInviteDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _sharingService.ResendInviteAsync(userId, dto);
        return Ok(result);
    }
}
