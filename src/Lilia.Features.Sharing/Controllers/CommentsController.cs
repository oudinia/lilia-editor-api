using Lilia.Api.Directories;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Api.Features.Sharing.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Comments on a document and their replies. Commenting is for everyone the document is shared with,
/// viewers included (2 Nov 2026, the owner: "Commenting should be for all. Not considered only as
/// editing"): every route needs read access. Changing or deleting a comment stays with its author or
/// the document's owner; a reply is deleted by its author.
/// </summary>
[ApiController]
[Route("api/documents/{docId:guid}/comments")]
[Authorize]
public class CommentsController : ControllerBase
{
    private readonly SharingDbContext _db;
    // Only the access check (3 Nov 2026), not the whole DocumentService.
    private readonly IDocumentAccess _access;
    private readonly IDocumentDirectory _documents;
    private readonly IPeopleDirectory _people;
    private readonly ILogger<CommentsController> _logger;

    public CommentsController(SharingDbContext db, IDocumentAccess access, IDocumentDirectory documents, IPeopleDirectory people, ILogger<CommentsController> logger)
    {
        _db = db;
        _documents = documents;
        _people = people;
        _access = access;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// List all comments for a document with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CommentDto>>> ListComments(
        Guid docId,
        [FromQuery] Guid? blockId,
        [FromQuery] bool? resolved)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        var query = _db.Comments
            .Include(c => c.Replies)
            .Where(c => c.DocumentId == docId);

        if (blockId.HasValue)
            query = query.Where(c => c.BlockId == blockId.Value);

        if (resolved.HasValue)
            query = query.Where(c => c.Resolved == resolved.Value);

        var comments = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(await ToDtosAsync(comments));
    }

    /// <summary>
    /// Get a single comment with replies.
    /// </summary>
    [HttpGet("{commentId:guid}")]
    public async Task<ActionResult<CommentDto>> GetComment(Guid docId, Guid commentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        var comment = await _db.Comments
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == docId);

        if (comment == null) return NotFound();

        return Ok((await ToDtosAsync([comment]))[0]);
    }

    /// <summary>
    /// Create a comment on a document.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CommentDto>> CreateComment(Guid docId, [FromBody] CreateCommentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest("Content is required.");

        // Validate that the referenced block exists and belongs to this document
        // before inserting — otherwise the FK violation bubbles up as a 500.
        if (dto.BlockId.HasValue)
        {
            if (!await _documents.HasBlockAsync(docId, dto.BlockId.Value))
                return NotFound("Block not found in this document.");
        }

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            BlockId = dto.BlockId,
            UserId = userId,
            Content = dto.Content.Trim(),
            Resolved = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();

        var created = await _db.Comments
            .Include(c => c.Replies)
            .FirstAsync(c => c.Id == comment.Id);

        _logger.LogInformation("Comment {CommentId} created on document {DocumentId} by user {UserId}",
            comment.Id, docId, userId);

        return CreatedAtAction(nameof(GetComment), new { docId, commentId = comment.Id }, (await ToDtosAsync([created]))[0]);
    }

    /// <summary>
    /// Update comment content or resolve/unresolve. Only comment author or document owner.
    /// </summary>
    [HttpPatch("{commentId:guid}")]
    public async Task<ActionResult<CommentDto>> UpdateComment(Guid docId, Guid commentId, [FromBody] UpdateCommentDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        var comment = await _db.Comments
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == docId);

        if (comment == null) return NotFound();

        // Only comment author or document owner can update
        var document = await _documents.FindAsync(docId);
        if (comment.UserId != userId && document?.OwnerId != userId)
            return Forbid();

        if (dto.Content != null)
            comment.Content = dto.Content.Trim();

        if (dto.Resolved.HasValue)
            comment.Resolved = dto.Resolved.Value;

        comment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok((await ToDtosAsync([comment]))[0]);
    }

    /// <summary>
    /// Delete a comment and all its replies. Only comment author or document owner.
    /// </summary>
    [HttpDelete("{commentId:guid}")]
    public async Task<ActionResult> DeleteComment(Guid docId, Guid commentId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        var comment = await _db.Comments
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == docId);

        if (comment == null) return NotFound();

        // Only comment author or document owner can delete
        var document = await _documents.FindAsync(docId);
        if (comment.UserId != userId && document?.OwnerId != userId)
            return Forbid();

        _db.CommentReplies.RemoveRange(comment.Replies);
        _db.Comments.Remove(comment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} deleted from document {DocumentId} by user {UserId}",
            commentId, docId, userId);

        return NoContent();
    }

    /// <summary>
    /// Add a reply to a comment.
    /// </summary>
    [HttpPost("{commentId:guid}/replies")]
    public async Task<ActionResult<CommentReplyDto>> CreateReply(Guid docId, Guid commentId, [FromBody] CreateReplyDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest("Content is required.");

        var comment = await _db.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == docId);

        if (comment == null) return NotFound();

        var reply = new CommentReply
        {
            Id = Guid.NewGuid(),
            CommentId = commentId,
            UserId = userId,
            Content = dto.Content.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.CommentReplies.Add(reply);
        await _db.SaveChangesAsync();

        var author = await _people.FindAsync(userId);

        _logger.LogInformation("Reply {ReplyId} added to comment {CommentId} by user {UserId}",
            reply.Id, commentId, userId);

        return CreatedAtAction(nameof(GetComment), new { docId, commentId }, MapReplyToDto(reply, author?.Name));
    }

    /// <summary>
    /// Delete a reply. Only reply author can delete.
    /// </summary>
    [HttpDelete("{commentId:guid}/replies/{replyId:guid}")]
    public async Task<ActionResult> DeleteReply(Guid docId, Guid commentId, Guid replyId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        var reply = await _db.CommentReplies
            .FirstOrDefaultAsync(r => r.Id == replyId && r.CommentId == commentId);

        if (reply == null) return NotFound();

        // Verify the comment belongs to this document
        var comment = await _db.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == docId);

        if (comment == null) return NotFound();

        // Only reply author can delete their reply
        if (reply.UserId != userId)
            return Forbid();

        _db.CommentReplies.Remove(reply);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Reply {ReplyId} deleted from comment {CommentId} by user {UserId}",
            replyId, commentId, userId);

        return NoContent();
    }

    /// <summary>
    /// Get comment counts for a document.
    /// </summary>
    [HttpGet("count")]
    public async Task<ActionResult<object>> GetCommentCounts(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _access.CanAsync(docId, userId, Permissions.Read))
            return Forbid();

        var total = await _db.Comments.CountAsync(c => c.DocumentId == docId);
        var resolved = await _db.Comments.CountAsync(c => c.DocumentId == docId && c.Resolved);

        return Ok(new { total, resolved, unresolved = total - resolved });
    }

    /// <summary>The comments with their authors' names, which come from Accounts (one lookup for them all).</summary>
    private async Task<List<CommentDto>> ToDtosAsync(IReadOnlyCollection<Comment> comments)
    {
        var people = await _people.FindManyAsync(comments.Select(c => c.UserId).Concat(comments.SelectMany(c => c.Replies.Select(r => r.UserId))));
        string? NameOf(string userId) => people.GetValueOrDefault(userId)?.Name;
        return comments.Select(c => new CommentDto(
            Id: c.Id,
            DocumentId: c.DocumentId,
            BlockId: c.BlockId,
            UserId: c.UserId,
            UserName: NameOf(c.UserId),
            Content: c.Content,
            Resolved: c.Resolved,
            Replies: c.Replies
                .OrderBy(r => r.CreatedAt)
                .Select(r => MapReplyToDto(r, NameOf(r.UserId)))
                .ToList(),
            CreatedAt: c.CreatedAt,
            UpdatedAt: c.UpdatedAt
        )).ToList();
    }

    private static CommentReplyDto MapReplyToDto(CommentReply r, string? userName)
    {
        return new CommentReplyDto(
            Id: r.Id,
            UserId: r.UserId,
            UserName: userName,
            Content: r.Content,
            CreatedAt: r.CreatedAt
        );
    }
}
