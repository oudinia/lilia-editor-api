using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiService _aiService;
    private readonly ILogger<AiController> _logger;

    private static readonly HashSet<string> ValidImproveActions = ["improve", "paraphrase", "expand", "shorten"];

    public AiController(IAiService aiService, ILogger<AiController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    private string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    // --- AI Features ---

    [HttpPost("generate-block")]
    public async Task<IActionResult> GenerateBlock([FromBody] GenerateBlockRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 2000)
            return BadRequest(new { error = "Prompt is required and must be at most 2000 characters." });

        _logger.LogInformation("User {UserId} generating block", userId);
        var result = await _aiService.GenerateBlockAsync(request.Prompt, request.Context);
        return Ok(new { block = new { result.Type, result.Content } });
    }

    [HttpPost("improve-text")]
    public async Task<IActionResult> ImproveText([FromBody] ImproveTextRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 10000)
            return BadRequest(new { error = "Text is required and must be at most 10000 characters." });
        if (!ValidImproveActions.Contains(request.Action))
            return BadRequest(new { error = "Action must be one of: improve, paraphrase, expand, shorten." });

        _logger.LogInformation("User {UserId} improving text with action {Action}", userId, request.Action);
        var result = await _aiService.ImproveTextAsync(request.Text, request.Action);
        return Ok(result);
    }

    [HttpPost("suggest-equation")]
    public async Task<IActionResult> SuggestEquation([FromBody] SuggestEquationRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 1000)
            return BadRequest(new { error = "Description is required and must be at most 1000 characters." });

        _logger.LogInformation("User {UserId} suggesting equation", userId);
        var result = await _aiService.SuggestEquationAsync(request.Description);
        return Ok(result);
    }

    [HttpPost("grammar-check")]
    public async Task<IActionResult> GrammarCheck([FromBody] GrammarCheckRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 10000)
            return BadRequest(new { error = "Text is required and must be at most 10000 characters." });

        _logger.LogInformation("User {UserId} running grammar check", userId);
        var result = await _aiService.GrammarCheckAsync(request.Text);
        return Ok(result);
    }

    [HttpPost("citation-check")]
    public async Task<IActionResult> CitationCheck([FromBody] CitationCheckRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 10000)
            return BadRequest(new { error = "Text is required and must be at most 10000 characters." });

        _logger.LogInformation("User {UserId} running citation check", userId);
        var result = await _aiService.CitationCheckAsync(request.Text);
        return Ok(result);
    }

    [HttpPost("generate-abstract")]
    public async Task<IActionResult> GenerateAbstract([FromBody] GenerateAbstractRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 50000)
            return BadRequest(new { error = "Content is required and must be at most 50000 characters." });

        _logger.LogInformation("User {UserId} generating abstract", userId);
        var result = await _aiService.GenerateAbstractAsync(request.Title, request.Content);
        return Ok(result);
    }

    // --- Chat CRUD ---

    [HttpPost("chats")]
    public async Task<IActionResult> CreateChat([FromBody] CreateAiChatRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chat = await _aiService.CreateChatAsync(userId, request);
        return CreatedAtAction(nameof(GetChat), new { id = chat.Id }, chat);
    }

    [HttpGet("chats")]
    public async Task<IActionResult> GetChats([FromQuery] string? organizationId = null)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _aiService.GetChatsAsync(userId, organizationId);
        return Ok(result);
    }

    [HttpGet("chats/{id}")]
    public async Task<IActionResult> GetChat(string id)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chat = await _aiService.GetChatAsync(userId, id);
        if (chat == null) return NotFound();
        return Ok(chat);
    }

    [HttpPut("chats/{id}")]
    public async Task<IActionResult> UpdateChat(string id, [FromBody] UpdateAiChatRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chat = await _aiService.UpdateChatAsync(userId, id, request);
        if (chat == null) return NotFound();
        return Ok(chat);
    }

    [HttpDelete("chats/{id}")]
    public async Task<IActionResult> DeleteChat(string id)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var deleted = await _aiService.DeleteChatAsync(userId, id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
