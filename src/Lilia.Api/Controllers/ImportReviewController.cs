using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Lilia.Api.Hubs;
using Lilia.Api.Services;
using Lilia.Core.DTOs;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/import-review/sessions")]
[Authorize]
public class ImportReviewController : ControllerBase
{
    private readonly IImportReviewService _reviewService;
    private readonly IHubContext<ImportReviewHub> _hubContext;
    private readonly ILogger<ImportReviewController> _logger;

    public ImportReviewController(
        IImportReviewService reviewService,
        IHubContext<ImportReviewHub> hubContext,
        ILogger<ImportReviewController> logger)
    {
        _reviewService = reviewService;
        _hubContext = hubContext;
        _logger = logger;
    }

    private IClientProxy SessionGroup(Guid sessionId) =>
        _hubContext.Clients.Group($"review-{sessionId}");

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// List the user's in-progress review sessions — drives the "Reviews"
    /// dashboard so users can resume a session instead of re-importing.
    /// Excludes imported and cancelled sessions.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ReviewSessionSummaryDto>>> ListActiveSessions()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var sessions = await _reviewService.ListActiveSessionsAsync(userId);
        return Ok(sessions);
    }

    /// <summary>
    /// Create a new import review session
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateSessionResponseDto>> CreateSession([FromBody] CreateReviewSessionDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var result = await _reviewService.CreateSessionAsync(userId, dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImportReview] Failed to create session");
            return StatusCode(500, new { message = "Failed to create review session" });
        }
    }

    /// <summary>
    /// Load a review session with all data
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SessionDataDto>> GetSession(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var session = await _reviewService.GetSessionAsync(id, userId);
        if (session == null) return NotFound();

        return Ok(session);
    }

    /// <summary>
    /// Cancel or permanently delete a review session
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> CancelSession(Guid id, [FromQuery] bool permanent = false)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.CancelSessionAsync(id, userId, permanent);
        if (!result) return NotFound();

        await SessionGroup(id).SendAsync("SessionCancelled", new { userId });

        return NoContent();
    }

    /// <summary>
    /// Finalize a review session — create document from reviewed blocks
    /// </summary>
    [HttpPost("{id:guid}/finalize")]
    public async Task<ActionResult<FinalizeResultDto>> FinalizeSession(Guid id, [FromBody] FinalizeSessionDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var result = await _reviewService.FinalizeSessionAsync(id, userId, dto);
            if (result == null) return BadRequest(new { message = "Cannot finalize session. Ensure all blocks are reviewed or use force=true." });

            await SessionGroup(id).SendAsync("SessionFinalized", new { documentId = result.Document.Id, userId });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImportReview] Failed to finalize session {SessionId}", id);
            return StatusCode(500, new { message = "Failed to finalize review session" });
        }
    }

    /// <summary>
    /// Update a block's review status or content
    /// </summary>
    [HttpPatch("{id:guid}/blocks/{blockId}")]
    public async Task<ActionResult<BlockReviewDto>> UpdateBlock(Guid id, string blockId, [FromBody] UpdateBlockReviewDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.UpdateBlockReviewAsync(id, blockId, userId, dto);
        if (result == null) return NotFound();

        await SessionGroup(id).SendAsync("BlockUpdated", new
        {
            blockId,
            status = result.Status,
            reviewedBy = result.ReviewedBy,
            reviewedAt = result.ReviewedAt,
            currentContent = result.CurrentContent,
            currentType = result.CurrentType,
            userId
        });

        return Ok(result);
    }

    /// <summary>
    /// Reset a block to its original imported state
    /// </summary>
    [HttpPost("{id:guid}/blocks/{blockId}/reset")]
    public async Task<ActionResult<BlockReviewDto>> ResetBlock(Guid id, string blockId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.ResetBlockAsync(id, blockId, userId);
        if (result == null) return NotFound();

        await SessionGroup(id).SendAsync("BlockReset", new { blockId, userId });

        return Ok(result);
    }

    /// <summary>
    /// Perform a bulk action on blocks (approveAll, rejectErrors, etc.)
    /// </summary>
    [HttpPost("{id:guid}/bulk-action")]
    public async Task<ActionResult> BulkAction(Guid id, [FromBody] BulkActionDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var affected = await _reviewService.BulkActionAsync(id, userId, dto);

        await SessionGroup(id).SendAsync("BulkActionPerformed", new
        {
            action = dto.Action,
            affectedCount = affected,
            userId
        });

        return Ok(new { affected });
    }

    /// <summary>
    /// Tier 1 bulk-convert against staged blocks in the review session:
    /// fold N into a list / ordered list, merge into paragraph, or re-level
    /// a run of headings. Frontend surfaces this in the review UI so users
    /// can fix "3 headings that should be a list" before finalize.
    /// </summary>
    [HttpPost("{id:guid}/batch-convert")]
    public async Task<ActionResult<BatchConvertResultDto>> BatchConvert(Guid id, [FromBody] BatchConvertReviewBlocksDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (dto.BlockIds == null || dto.BlockIds.Count == 0)
            return BadRequest(new { code = "EMPTY_SELECTION", message = "blockIds is required." });

        var result = await _reviewService.BatchConvertBlockReviewsAsync(id, userId, dto);
        if (result == null)
            return BadRequest(new { code = "INVALID_ACTION_OR_BLOCKS", message = "Unknown action, missing blocks, or reheading level out of range." });

        await SessionGroup(id).SendAsync("BulkConverted", new { action = dto.Action, blockIds = dto.BlockIds, userId });
        return Ok(result);
    }

    /// <summary>
    /// Add a collaborator to the review session
    /// </summary>
    [HttpPost("{id:guid}/collaborators")]
    public async Task<ActionResult<CollaboratorResponseDto>> AddCollaborator(Guid id, [FromBody] AddReviewCollaboratorDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.AddCollaboratorAsync(id, userId, dto);
        if (result == null) return BadRequest(new { message = "Could not add collaborator. User may not exist or is already a collaborator." });

        await SessionGroup(id).SendAsync("CollaboratorAdded", new { collaborator = result });

        return Ok(new CollaboratorResponseDto(result));
    }

    /// <summary>
    /// Remove a collaborator from the review session
    /// </summary>
    [HttpDelete("{id:guid}/collaborators/{targetUserId}")]
    public async Task<ActionResult> RemoveCollaborator(Guid id, string targetUserId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.RemoveCollaboratorAsync(id, targetUserId, userId);
        if (!result) return NotFound();

        await SessionGroup(id).SendAsync("CollaboratorRemoved", new { userId = targetUserId, removedBy = userId });

        return NoContent();
    }

    /// <summary>
    /// Add a comment to a block in the review
    /// </summary>
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentResponseDto>> AddComment(Guid id, [FromBody] AddReviewCommentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.AddCommentAsync(id, userId, dto);
        if (result == null) return NotFound();

        await SessionGroup(id).SendAsync("CommentAdded", new { comment = result, blockId = dto.BlockId });

        return Ok(new CommentResponseDto(result));
    }

    /// <summary>
    /// Resolve or unresolve a comment
    /// </summary>
    [HttpPatch("{id:guid}/comments/{commentId:guid}")]
    public async Task<ActionResult> UpdateComment(Guid id, Guid commentId, [FromBody] UpdateReviewCommentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.UpdateCommentAsync(id, commentId, userId, dto);
        if (!result) return NotFound();

        await SessionGroup(id).SendAsync("CommentUpdated", new { commentId, resolved = dto.Resolved, userId });

        return NoContent();
    }

    /// <summary>
    /// Delete a comment
    /// </summary>
    [HttpDelete("{id:guid}/comments/{commentId:guid}")]
    public async Task<ActionResult> DeleteComment(Guid id, Guid commentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.DeleteCommentAsync(id, commentId, userId);
        if (!result) return NotFound();

        await SessionGroup(id).SendAsync("CommentDeleted", new { commentId, userId });

        return NoContent();
    }

    /// <summary>
    /// Get comments for a session, optionally filtered by block
    /// </summary>
    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<CommentsListDto>> GetComments(Guid id, [FromQuery] string? blockId = null)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var comments = await _reviewService.GetCommentsAsync(id, userId, blockId);
        return Ok(new CommentsListDto(comments));
    }

    /// <summary>
    /// Get activity feed for a review session
    /// </summary>
    [HttpGet("{id:guid}/activity")]
    public async Task<ActionResult<ActivitiesListDto>> GetActivities(Guid id, [FromQuery] int limit = 50)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var activities = await _reviewService.GetActivitiesAsync(id, userId, limit);
        return Ok(new ActivitiesListDto(activities));
    }

    /// <summary>
    /// Poll for recent activities since a given timestamp
    /// </summary>
    [HttpGet("{id:guid}/activity/recent")]
    public async Task<ActionResult<ActivitiesListDto>> GetRecentActivities(Guid id, [FromQuery] DateTime since)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var activities = await _reviewService.GetRecentActivitiesAsync(id, userId, since);
        return Ok(new ActivitiesListDto(activities));
    }

    /// <summary>
    /// Get paragraph-level diagnostic traces for an import session.
    /// Shows every body element from the DOCX, what rule matched, and what type was detected.
    /// </summary>
    [HttpGet("{id:guid}/traces")]
    public async Task<ActionResult> GetParagraphTraces(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var traces = await _reviewService.GetParagraphTracesAsync(id, userId);
        if (traces == null) return NotFound();

        return Ok(traces);
    }

    /// <summary>
    /// List all diagnostics (parser issues, load-order traps, shim notifications)
    /// for an import session. Ordered by severity then source line.
    /// </summary>
    [HttpGet("{id:guid}/diagnostics")]
    public async Task<ActionResult<List<ImportDiagnosticDto>>> GetDiagnostics(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var diagnostics = await _reviewService.GetDiagnosticsAsync(id, userId);
        return Ok(diagnostics);
    }

    /// <summary>
    /// Dismiss a diagnostic (acknowledge; it still exists for audit but no
    /// longer shows up in active badge counts).
    /// </summary>
    [HttpPost("{id:guid}/diagnostics/{diagnosticId:guid}/dismiss")]
    public async Task<ActionResult<ImportDiagnosticDto>> DismissDiagnostic(Guid id, Guid diagnosticId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _reviewService.DismissDiagnosticAsync(id, diagnosticId, userId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// List structural findings for a session. Rows are persisted in
    /// import_structural_findings — this endpoint is a pure SELECT.
    /// Call POST .../hints/compute to (re)run the rule pipeline.
    /// </summary>
    [HttpGet("{id:guid}/hints")]
    public async Task<ActionResult<List<ImportStructuralFindingDto>>> ListHints(Guid id, [FromServices] IImportHintService hintService)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var hints = await hintService.ListForSessionAsync(id, userId);
        return Ok(hints);
    }

    /// <summary>Recompute findings for this session. Clears pending rows, re-runs rules, bulk-inserts the new set.</summary>
    [HttpPost("{id:guid}/hints/compute")]
    public async Task<ActionResult<ComputeHintsResponseDto>> ComputeHints(Guid id, [FromServices] IImportHintService hintService)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var count = await hintService.ComputeForSessionAsync(id, userId);
        return Ok(new ComputeHintsResponseDto(count));
    }

    /// <summary>Mark a structural finding applied.</summary>
    [HttpPost("{id:guid}/hints/{findingId:guid}/apply")]
    public async Task<IActionResult> ApplyHint(Guid id, Guid findingId, [FromServices] IImportHintService hintService)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var ok = await hintService.ApplyAsync(findingId, userId);
        return ok ? Ok() : NotFound();
    }

    /// <summary>Dismiss a structural finding.</summary>
    [HttpPost("{id:guid}/hints/{findingId:guid}/dismiss")]
    public async Task<IActionResult> DismissHint(Guid id, Guid findingId, [FromServices] IImportHintService hintService)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var ok = await hintService.DismissAsync(findingId, userId);
        return ok ? Ok() : NotFound();
    }

    /// <summary>Set the document category on a session (cv | thesis | report | research | business). Nullable.</summary>
    [HttpPatch("{id:guid}/category")]
    public async Task<IActionResult> SetSessionCategory(Guid id, [FromBody] SetDocumentCategoryDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var updated = await _reviewService.SetSessionCategoryAsync(id, userId, dto.Category);
        return updated ? Ok() : NotFound();
    }
}
