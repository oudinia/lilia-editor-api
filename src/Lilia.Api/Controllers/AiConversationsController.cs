using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Durable Ask Lilia conversations. Owner-gated; scoped by document with
/// move/clone across documents. The client treats these as the source of truth
/// (localStorage is only an optimistic cache).
/// </summary>
[ApiController]
[Route("api/ai/conversations")]
[Authorize]
public class AiConversationsController : ControllerBase
{
    private readonly IAiConversationService _conversations;

    public AiConversationsController(IAiConversationService conversations)
    {
        _conversations = conversations;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>List the caller's conversations, optionally filtered to one document.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AiConversationListDto>>> List([FromQuery] Guid? documentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _conversations.ListAsync(userId, documentId));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AiConversationDto>> Get(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var conv = await _conversations.GetAsync(userId, id);
        return conv is null ? NotFound() : Ok(conv);
    }

    [HttpPost]
    public async Task<ActionResult<AiConversationDto>> Create([FromBody] CreateConversationDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            var conv = await _conversations.CreateAsync(userId, dto);
            return CreatedAtAction(nameof(Get), new { id = conv.Id }, conv);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<AiMessageDto>> AppendMessage(Guid id, [FromBody] AppendMessageDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var msg = await _conversations.AppendMessageAsync(userId, id, dto);
        return msg is null ? NotFound() : Ok(msg);
    }

    [HttpPatch("{id:guid}/rename")]
    public async Task<ActionResult> Rename(Guid id, [FromBody] RenameConversationDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return await _conversations.RenameAsync(userId, id, dto.Title) ? NoContent() : NotFound();
    }

    /// <summary>Move (reassign) the conversation to another document, or null = general.</summary>
    [HttpPatch("{id:guid}/move")]
    public async Task<ActionResult> Move(Guid id, [FromBody] MoveConversationDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            return await _conversations.MoveAsync(userId, id, dto.DocumentId) ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    /// <summary>Clone (copy/promote) the conversation under another document.</summary>
    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<AiConversationDto>> Clone(Guid id, [FromBody] CloneConversationDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            var conv = await _conversations.CloneAsync(userId, id, dto);
            return conv is null ? NotFound() : CreatedAtAction(nameof(Get), new { id = conv.Id }, conv);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return await _conversations.DeleteAsync(userId, id) ? NoContent() : NotFound();
    }
}
