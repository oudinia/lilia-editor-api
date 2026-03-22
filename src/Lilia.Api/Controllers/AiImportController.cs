using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lilia.Api.Services;
using Lilia.Core.Models.AiImport;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/import-review/sessions/{sessionId:guid}/ai")]
[Authorize]
public class AiImportController : ControllerBase
{
    private readonly IAiImportService _aiImportService;
    private readonly IImportReviewService _reviewService;
    private readonly ILogger<AiImportController> _logger;

    public AiImportController(
        IAiImportService aiImportService,
        IImportReviewService reviewService,
        ILogger<AiImportController> logger)
    {
        _aiImportService = aiImportService;
        _reviewService = reviewService;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Classify all blocks in a review session using AI or heuristics.
    /// Returns suggested type changes with confidence scores.
    /// </summary>
    [HttpPost("classify")]
    public async Task<ActionResult<ClassifySessionResponse>> ClassifySession(Guid sessionId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            // Load the session to get blocks
            var session = await _reviewService.GetSessionAsync(sessionId, userId);
            if (session == null) return NotFound();

            // Extract content and type from each block
            var blocks = session.Blocks.Select(b =>
            {
                var content = ExtractTextContent(b.CurrentContent ?? b.OriginalContent);
                var type = b.CurrentType ?? b.OriginalType;
                return (content, type);
            }).ToList();

            _logger.LogInformation(
                "[AiImport] Classifying {Count} blocks for session {SessionId}",
                blocks.Count, sessionId);

            var classifications = await _aiImportService.ClassifyBatchAsync(blocks);

            // Attach block IDs to the classifications
            var enriched = classifications.Select((c, i) => c with { BlockId = session.Blocks[i].BlockId }).ToList();
            var suggestedChanges = enriched.Count(c => c.SuggestsChange);

            return Ok(new ClassifySessionResponse(sessionId, enriched, blocks.Count, suggestedChanges));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiImport] Failed to classify blocks for session {SessionId}", sessionId);
            return StatusCode(500, new { message = "Failed to classify blocks" });
        }
    }

    /// <summary>
    /// Get AI-powered quality suggestions for a specific block.
    /// </summary>
    [HttpPost("suggest/{blockId}")]
    public async Task<ActionResult<BlockSuggestionsResponse>> SuggestImprovements(Guid sessionId, string blockId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var session = await _reviewService.GetSessionAsync(sessionId, userId);
            if (session == null) return NotFound();

            var block = session.Blocks.FirstOrDefault(b => b.BlockId == blockId);
            if (block == null) return NotFound(new { message = $"Block {blockId} not found in session" });

            var content = ExtractTextContent(block.CurrentContent ?? block.OriginalContent);
            var blockType = block.CurrentType ?? block.OriginalType;

            _logger.LogInformation(
                "[AiImport] Suggesting improvements for block {BlockId} (type: {BlockType}) in session {SessionId}",
                blockId, blockType, sessionId);

            var suggestions = await _aiImportService.SuggestImprovementsAsync(content, blockType);

            return Ok(new BlockSuggestionsResponse(blockId, blockType, suggestions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiImport] Failed to suggest improvements for block {BlockId} in session {SessionId}",
                blockId, sessionId);
            return StatusCode(500, new { message = "Failed to generate suggestions" });
        }
    }

    /// <summary>
    /// Auto-fix formatting issues in a specific block's content.
    /// Returns the fixed content without modifying the session.
    /// </summary>
    [HttpPost("fix/{blockId}")]
    public async Task<ActionResult<BlockFixResponse>> FixFormatting(Guid sessionId, string blockId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var session = await _reviewService.GetSessionAsync(sessionId, userId);
            if (session == null) return NotFound();

            var block = session.Blocks.FirstOrDefault(b => b.BlockId == blockId);
            if (block == null) return NotFound(new { message = $"Block {blockId} not found in session" });

            var content = ExtractTextContent(block.CurrentContent ?? block.OriginalContent);
            var blockType = block.CurrentType ?? block.OriginalType;

            _logger.LogInformation(
                "[AiImport] Fixing formatting for block {BlockId} (type: {BlockType}) in session {SessionId}",
                blockId, blockType, sessionId);

            var fixedContent = await _aiImportService.FixFormattingAsync(content, blockType);
            var wasModified = fixedContent != content;

            var changes = new List<string>();
            if (wasModified)
            {
                if (fixedContent.Length != content.Length)
                    changes.Add($"Content length changed from {content.Length} to {fixedContent.Length}");
                if (content.Contains("\r\n") && !fixedContent.Contains("\r\n"))
                    changes.Add("Normalized line endings");
                if (content.Contains("  ") && !fixedContent.Contains("  "))
                    changes.Add("Collapsed multiple spaces");
            }

            return Ok(new BlockFixResponse(blockId, content, fixedContent, wasModified, changes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiImport] Failed to fix formatting for block {BlockId} in session {SessionId}",
                blockId, sessionId);
            return StatusCode(500, new { message = "Failed to fix formatting" });
        }
    }

    /// <summary>
    /// Extract plain text content from a block's JSON content element.
    /// </summary>
    private static string ExtractTextContent(JsonElement content)
    {
        // If it's a string, return it directly
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

        // If it's an object, look for common content properties
        if (content.ValueKind == JsonValueKind.Object)
        {
            // Try "text" property first (most block types)
            if (content.TryGetProperty("text", out var text))
                return text.GetString() ?? "";

            // Try "content" property
            if (content.TryGetProperty("content", out var innerContent))
            {
                if (innerContent.ValueKind == JsonValueKind.String)
                    return innerContent.GetString() ?? "";
            }

            // Try "latex" for equation blocks
            if (content.TryGetProperty("latex", out var latex))
                return latex.GetString() ?? "";

            // Try "code" for code blocks
            if (content.TryGetProperty("code", out var code))
                return code.GetString() ?? "";

            // Fallback: serialize the whole object
            return content.GetRawText();
        }

        return content.GetRawText();
    }
}
