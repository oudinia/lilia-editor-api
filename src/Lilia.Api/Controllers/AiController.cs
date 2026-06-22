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
    private readonly IAiCatalogService _catalog;
    private readonly IEntitlementService _entitlements;

    private static readonly HashSet<string> ValidImproveActions = ["improve", "paraphrase", "expand", "shorten"];

    public AiController(
        IAiService aiService,
        ILogger<AiController> logger,
        IAiCatalogService catalog,
        IEntitlementService entitlements)
    {
        _aiService = aiService;
        _logger = logger;
        _catalog = catalog;
        _entitlements = entitlements;
    }

    private string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// The selectable AI models for the editor's model picker. Returns every
    /// enabled model with its tier/capability metadata and a <c>usable</c> flag
    /// computed from the caller's membership, plus the default model id.
    /// </summary>
    [HttpGet("models")]
    public async Task<IActionResult> GetModels(CancellationToken ct)
    {
        var userId = GetUserId();
        var membership = "free";
        if (userId is not null)
        {
            var plan = await _entitlements.GetActivePlanAsync(userId, ct);
            // Plan slugs (free/beta/conversion/pro/team) → coarse tier. Anything
            // that isn't pro/team is treated as free for model gating.
            var slug = plan?.Slug?.ToLowerInvariant();
            membership = slug is "pro" or "team" ? slug : "free";
        }

        var models = _catalog.Enabled().Select(m => new
        {
            id = m.Id,
            provider = m.Provider,
            displayName = m.DisplayName,
            tier = m.TierLabel,
            minMembership = m.MinMembership,
            contextWindow = m.ContextWindow,
            maxOutput = m.MaxOutput,
            supportsAttachments = m.SupportsAttachments,
            supportsVision = m.SupportsVision,
            isDefault = m.IsDefault,
            usable = _catalog.IsAllowedFor(m.Id, membership),
        });

        var creditsUsed = userId is not null ? await _entitlements.GetAiCreditsConsumedAsync(userId, ct) : 0;
        return Ok(new { defaultModel = _catalog.DefaultModelId(), membership, creditsUsed, models });
    }

    public record EstimateRequest(string? Model, int? InputTokens, int? InputChars, int? OutputTokens);

    /// <summary>
    /// Pre-flight credit estimate for a prospective call — lets the editor show
    /// "≈ N credits" before sending. Input tokens ≈ chars/4; output defaults to
    /// a typical reply when not given.
    /// </summary>
    [HttpPost("estimate")]
    public IActionResult EstimateCredits([FromBody] EstimateRequest req)
    {
        var model = string.IsNullOrWhiteSpace(req.Model) ? _catalog.DefaultModelId() : req.Model!;
        var inTok = req.InputTokens ?? (req.InputChars is { } c ? (c + 3) / 4 : 0);
        var outTok = req.OutputTokens ?? 1000;   // typical reply when unknown
        var credits = _catalog.CreditsFor(model, inTok, outTok);
        return Ok(new { model, estimatedInputTokens = inTok, estimatedOutputTokens = outTok, credits });
    }

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
