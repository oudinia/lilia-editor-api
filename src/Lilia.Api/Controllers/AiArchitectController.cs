using Lilia.Api.Models.AiArchitect;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Hosted AI Document-Architect. Converses with the author and proposes typed
/// block operations for a document. Read-only with respect to the document —
/// the editor applies accepted operations. Stateless: the conversation is
/// supplied by the client on every call.
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiArchitectController : ControllerBase
{
    private readonly IAiArchitectService _architect;
    private readonly ILogger<AiArchitectController> _logger;

    public AiArchitectController(IAiArchitectService architect, ILogger<AiArchitectController> logger)
    {
        _architect = architect;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// POST /api/ai/architect — propose block operations for a document given a
    /// conversation. Returns 200 with { reply, operations, usage, balance } on
    /// success, or 403 with { locked, reason, message } when gated.
    /// </summary>
    [HttpPost("architect")]
    [ProducesResponseType(typeof(AiArchitectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AiArchitectLocked), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Architect([FromBody] AiArchitectRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (request is null || string.IsNullOrWhiteSpace(request.DocumentId))
            return BadRequest(new { message = "DocumentId is required." });
        if (request.Messages is null || request.Messages.Count == 0)
            return BadRequest(new { message = "At least one message is required." });

        try
        {
            var outcome = await _architect.ArchitectAsync(userId, request, ct);
            if (outcome.IsLocked)
                return StatusCode(StatusCodes.Status403Forbidden, outcome.Lock_);

            return Ok(outcome.Response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiArchitect] Request failed for user {UserId}", userId);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "The AI service failed to respond. Please try again." });
        }
    }
}
