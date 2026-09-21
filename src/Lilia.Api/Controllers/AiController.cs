using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
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
    // Not a second verifier — the same two services /api/convert/block/validate
    // composes, in the same order. The claim "this compiles" has one
    // implementation, and this asks it rather than reimplementing it.
    private readonly IRenderService _renderService;
    private readonly ILatexVerifier _latexVerifier;

    private static readonly HashSet<string> ValidImproveActions = ["improve", "paraphrase", "expand", "shorten"];

    public AiController(
        IAiService aiService,
        ILogger<AiController> logger,
        IAiCatalogService catalog,
        IEntitlementService entitlements,
        IRenderService renderService,
        ILatexVerifier latexVerifier)
    {
        _aiService = aiService;
        _logger = logger;
        _catalog = catalog;
        _entitlements = entitlements;
        _renderService = renderService;
        _latexVerifier = latexVerifier;
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

    /// <summary>
    /// Revise a block the author already has, and — unless they say not to —
    /// compile the result before handing it back.
    ///
    /// <para>The compiler is the point. A model asked to merge two columns of a
    /// table will sometimes return LaTeX that does not build, and a tool whose
    /// answer is "here, paste this" makes that the author's problem. Here the
    /// failure goes back to the model with the compiler's own error attached,
    /// once. If the second attempt also fails, the block still comes back, with
    /// <c>verified: false</c> and the error — the author decides, but they are
    /// never told a broken table is fine.</para>
    ///
    /// <para>Block-generic because the backend is: the table tool is the first
    /// caller, not the only one it will ever have.</para>
    /// </summary>
    [HttpPost("block/revise")]
    public async Task<IActionResult> ReviseBlock([FromBody] ReviseBlockRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Type))
            return BadRequest(new { error = "Block type is required." });
        if (string.IsNullOrWhiteSpace(request.Instruction) || request.Instruction.Length > 2000)
            return BadRequest(new { error = "Instruction is required and must be at most 2000 characters." });
        if (request.Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return BadRequest(new { error = "Block content is required." });

        LatexEngine? requested = null;
        if (!string.IsNullOrWhiteSpace(request.Engine)
            && Enum.TryParse<LatexEngine>(request.Engine, ignoreCase: true, out var parsed))
        {
            requested = parsed;
        }

        _logger.LogInformation("User {UserId} revising a {Type} block", userId, request.Type);

        ReviseBlockResult result;
        try
        {
            result = await _aiService.ReviseBlockAsync(
                request.Type, request.Content, request.Instruction);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Ai] revise returned unparseable JSON for {Type}", request.Type);
            return Ok(new { error = "The assistant did not return a usable block.", code = "BAD_MODEL_OUTPUT" });
        }

        if (!request.Verify)
            return Ok(new ReviseBlockResponse(result.Type, result.Content, result.Note, false));

        var (ok, error) = await CompilesAsync(result.Type, result.Content, requested);
        if (ok || error is null)
            return Ok(new ReviseBlockResponse(result.Type, result.Content, result.Note, ok, error));

        // One retry, with the compiler's own words. Not a loop: if the model
        // cannot fix it with the error in hand, a third attempt is a slower way
        // to say the same thing, and the author is waiting.
        try
        {
            var second = await _aiService.ReviseBlockAsync(
                request.Type, request.Content, request.Instruction, error);
            var (ok2, error2) = await CompilesAsync(second.Type, second.Content, requested);
            return Ok(new ReviseBlockResponse(second.Type, second.Content, second.Note, ok2, error2, 2));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Ai] revise retry returned unparseable JSON for {Type}", request.Type);
            return Ok(new ReviseBlockResponse(result.Type, result.Content, result.Note, false, error, 2));
        }
    }

    /// <summary>
    /// Render the block and compile it. Returns (false, null) when no compiler
    /// was reachable: "unchecked" is not "invalid", and an unreachable compiler
    /// must not become a retry — there is nothing for the model to fix.
    /// </summary>
    private async Task<(bool Ok, string? Error)> CompilesAsync(
        string type, JsonElement content, LatexEngine? requested)
    {
        string latex;
        try
        {
            var block = new Block { Id = Guid.NewGuid(), Type = type, Content = JsonDocument.Parse(content.GetRawText()) };
            latex = _renderService.RenderBlockToLatex(block) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Ai] could not render a revised {Type} block", type);
            return (false, "The revised block could not be rendered to LaTeX.");
        }

        if (string.IsNullOrWhiteSpace(latex))
            return (false, "The revised block rendered to nothing.");

        var verdict = await _latexVerifier.VerifyAsync(latex, requested);
        if (verdict.Status == "verified") return (true, null);
        if (verdict.Status == "unchecked") return (false, null);
        return (false, string.Join("\n", verdict.Findings));
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
