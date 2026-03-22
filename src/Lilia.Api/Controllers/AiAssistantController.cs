using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lilia.Api.Services;
using Lilia.Core.Models.AiAssistant;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/ai")]
[Authorize]
public class AiAssistantController : ControllerBase
{
    private readonly IAiAssistantService _aiAssistantService;
    private readonly AiAssistantService _aiAssistantServiceImpl;
    private readonly ILogger<AiAssistantController> _logger;

    public AiAssistantController(
        IAiAssistantService aiAssistantService,
        ILogger<AiAssistantController> logger)
    {
        _aiAssistantService = aiAssistantService;
        // Cast to access rate limiting (internal method)
        _aiAssistantServiceImpl = (AiAssistantService)aiAssistantService;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Generate LaTeX from a natural language description.
    /// </summary>
    [HttpPost("math/generate")]
    public async Task<ActionResult<MathGenerationResult>> GenerateMath([FromBody] MathGenerateRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Description is required" });

        if (!_aiAssistantServiceImpl.CheckRateLimit(userId))
            return StatusCode(429, new { message = "Rate limit exceeded. Try again later." });

        try
        {
            _logger.LogInformation("[AiAssistant] Math generate for user {UserId}", userId);
            var result = await _aiAssistantService.GenerateMathAsync(request.Description, request.Context);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiAssistant] Failed to generate math for user {UserId}", userId);
            return StatusCode(500, new { message = "Failed to generate math expression" });
        }
    }

    /// <summary>
    /// Fix a broken LaTeX expression.
    /// </summary>
    [HttpPost("math/fix")]
    public async Task<ActionResult<MathFixResult>> FixMath([FromBody] MathFixRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.BrokenLatex))
            return BadRequest(new { message = "BrokenLatex is required" });

        if (!_aiAssistantServiceImpl.CheckRateLimit(userId))
            return StatusCode(429, new { message = "Rate limit exceeded. Try again later." });

        try
        {
            _logger.LogInformation("[AiAssistant] Math fix for user {UserId}", userId);
            var result = await _aiAssistantService.FixMathAsync(request.BrokenLatex, request.ErrorMessage);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiAssistant] Failed to fix math for user {UserId}", userId);
            return StatusCode(500, new { message = "Failed to fix math expression" });
        }
    }

    /// <summary>
    /// Improve academic writing with the specified action.
    /// </summary>
    [HttpPost("writing/improve")]
    public async Task<ActionResult<WritingResult>> ImproveWriting([FromBody] WritingImproveRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { message = "Text is required" });

        if (string.IsNullOrWhiteSpace(request.Action))
            return BadRequest(new { message = "Action is required" });

        if (!_aiAssistantServiceImpl.CheckRateLimit(userId))
            return StatusCode(429, new { message = "Rate limit exceeded. Try again later." });

        try
        {
            _logger.LogInformation("[AiAssistant] Writing improve ({Action}) for user {UserId}", request.Action, userId);
            var result = await _aiAssistantService.ImproveWritingAsync(request.Text, request.Action, request.Style);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiAssistant] Failed to improve writing for user {UserId}", userId);
            return StatusCode(500, new { message = "Failed to improve writing" });
        }
    }

    /// <summary>
    /// Classify raw text into a block type.
    /// </summary>
    [HttpPost("blocks/classify")]
    public async Task<ActionResult<BlockClassificationResult>> ClassifyBlock([FromBody] BlockClassifyRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Content is required" });

        if (!_aiAssistantServiceImpl.CheckRateLimit(userId))
            return StatusCode(429, new { message = "Rate limit exceeded. Try again later." });

        try
        {
            _logger.LogInformation("[AiAssistant] Block classify for user {UserId}", userId);
            var result = await _aiAssistantService.ClassifyBlockAsync(request.Content);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiAssistant] Failed to classify block for user {UserId}", userId);
            return StatusCode(500, new { message = "Failed to classify block" });
        }
    }
}
