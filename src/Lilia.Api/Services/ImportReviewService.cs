using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class ImportReviewService : IImportReviewService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<ImportReviewService> _logger;

    public ImportReviewService(LiliaDbContext context, ILogger<ImportReviewService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // --- Session Management ---

    public async Task<CreateSessionResponseDto> CreateSessionAsync(string userId, CreateReviewSessionDto dto)
    {
        var session = new ImportReviewSession
        {
            Id = Guid.NewGuid(),
            JobId = dto.JobId,
            OwnerId = userId,
            DocumentTitle = dto.DocumentTitle,
            Status = "in_progress",
            OriginalWarnings = dto.Warnings.HasValue
                ? JsonDocument.Parse(dto.Warnings.Value.GetRawText())
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _context.ImportReviewSessions.Add(session);

        // Create block reviews
        var blockReviews = new List<ImportBlockReview>();
        for (var i = 0; i < dto.Blocks.Count; i++)
        {
            var block = dto.Blocks[i];
            var review = new ImportBlockReview
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                BlockIndex = i,
                BlockId = block.Id,
                Status = "pending",
                OriginalContent = JsonDocument.Parse(block.Content.GetRawText()),
                OriginalType = block.Type,
                Confidence = block.Confidence,
                Warnings = block.Warnings.HasValue
                    ? JsonDocument.Parse(block.Warnings.Value.GetRawText())
                    : null,
                SortOrder = block.SortOrder,
                Depth = block.Depth
            };
            blockReviews.Add(review);
        }

        _context.ImportBlockReviews.AddRange(blockReviews);

        // Add owner as collaborator
        _context.ImportReviewCollaborators.Add(new ImportReviewCollaborator
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "owner",
            InvitedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        });

        // Log activity
        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Action = "session_created",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation("[ImportReview] Session {SessionId} created by {UserId} with {BlockCount} blocks",
            session.Id, userId, blockReviews.Count);

        var sessionDto = MapSessionToDto(session);
        var blockDtos = blockReviews.Select(br => MapBlockReviewToDto(br, 0, 0)).ToList();

        return new CreateSessionResponseDto(sessionDto, blockDtos);
    }

    public async Task<SessionDataDto?> GetSessionAsync(Guid sessionId, string userId)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Owner)
            .Include(s => s.Collaborators)
                .ThenInclude(c => c.User)
            .Include(s => s.BlockReviews)
                .ThenInclude(br => br.Reviewer)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        // Verify access
        var userRole = GetUserRole(session, userId);
        if (userRole == null) return null;

        // Update last active
        var collaborator = session.Collaborators.FirstOrDefault(c => c.UserId == userId);
        if (collaborator != null)
        {
            collaborator.LastActiveAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // Compute comment counts per block
        var commentCounts = await _context.ImportBlockComments
            .Where(c => c.SessionId == sessionId)
            .GroupBy(c => c.BlockId)
            .Select(g => new
            {
                BlockId = g.Key,
                Total = g.Count(),
                Unresolved = g.Count(c => !c.Resolved)
            })
            .ToDictionaryAsync(x => x.BlockId, x => new { x.Total, x.Unresolved });

        var ownerDto = new ReviewUserDto(session.Owner.Id, session.Owner.Name, session.Owner.Image);

        var blockDtos = session.BlockReviews
            .OrderBy(br => br.SortOrder)
            .Select(br =>
            {
                commentCounts.TryGetValue(br.BlockId, out var counts);
                return MapBlockReviewToDto(br, counts?.Total ?? 0, counts?.Unresolved ?? 0);
            })
            .ToList();

        var collaboratorDtos = session.Collaborators
            .Select(c => new CollaboratorInfoDto(
                c.UserId,
                c.Role,
                c.InvitedAt,
                c.LastActiveAt,
                new ReviewUserWithEmailDto(c.User.Id, c.User.Name, c.User.Email, c.User.Image)
            ))
            .ToList();

        var sessionDto = MapSessionToDto(session);

        return new SessionDataDto(sessionDto, ownerDto, blockDtos, collaboratorDtos, userRole);
    }

    public async Task<bool> CancelSessionAsync(Guid sessionId, string userId, bool permanent = false)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        var role = GetUserRole(session, userId);
        if (role != "owner") return false;

        if (permanent)
        {
            // Delete all related data
            var blockReviews = await _context.ImportBlockReviews.Where(b => b.SessionId == sessionId).ToListAsync();
            var comments = await _context.ImportBlockComments.Where(c => c.SessionId == sessionId).ToListAsync();
            var collaborators = await _context.ImportReviewCollaborators.Where(c => c.SessionId == sessionId).ToListAsync();
            var activities = await _context.ImportReviewActivities.Where(a => a.SessionId == sessionId).ToListAsync();

            _context.ImportBlockReviews.RemoveRange(blockReviews);
            _context.ImportBlockComments.RemoveRange(comments);
            _context.ImportReviewCollaborators.RemoveRange(collaborators);
            _context.ImportReviewActivities.RemoveRange(activities);
            _context.ImportReviewSessions.Remove(session);
        }
        else
        {
            session.Status = "cancelled";
            session.UpdatedAt = DateTime.UtcNow;

            _context.ImportReviewActivities.Add(new ImportReviewActivity
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = userId,
                Action = "session_cancelled",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("[ImportReview] Session {SessionId} {Action} by {UserId}",
            sessionId, permanent ? "deleted" : "cancelled", userId);

        return true;
    }

    public async Task<FinalizeResultDto?> FinalizeSessionAsync(Guid sessionId, string userId, FinalizeSessionDto dto)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .Include(s => s.BlockReviews)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        var role = GetUserRole(session, userId);
        if (role != "owner") return null;

        if (session.Status != "in_progress") return null;

        var blocks = session.BlockReviews.OrderBy(b => b.SortOrder).ToList();

        // Check if all blocks are reviewed (unless force)
        var pendingBlocks = blocks.Where(b => b.Status == "pending").ToList();
        if (pendingBlocks.Any() && !dto.Force)
        {
            return null; // Controller will return 400
        }

        // If force, treat pending blocks as approved
        if (dto.Force && pendingBlocks.Any())
        {
            foreach (var pending in pendingBlocks)
            {
                pending.Status = "approved";
                pending.ReviewedBy = userId;
                pending.ReviewedAt = DateTime.UtcNow;
            }
        }

        // Create document from approved/edited blocks (skip rejected)
        var approvedBlocks = blocks.Where(b => b.Status is "approved" or "edited").ToList();
        var rejectedCount = blocks.Count(b => b.Status == "rejected");

        var documentTitle = dto.DocumentTitle ?? session.DocumentTitle;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var document = new Document
            {
                Id = Guid.NewGuid(),
                OwnerId = userId,
                Title = documentTitle,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);

            var sortOrder = 0;
            foreach (var review in approvedBlocks)
            {
                var content = review.CurrentContent ?? review.OriginalContent;
                var type = review.CurrentType ?? review.OriginalType;

                var block = new Block
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    Type = type,
                    Content = JsonDocument.Parse(content.RootElement.GetRawText()),
                    SortOrder = sortOrder++,
                    Depth = review.Depth,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Blocks.Add(block);
            }

            // Update session
            session.Status = "imported";
            session.DocumentId = document.Id;
            session.UpdatedAt = DateTime.UtcNow;

            // Update job if linked
            if (session.JobId.HasValue)
            {
                var job = await _context.Jobs.FindAsync(session.JobId.Value);
                if (job != null)
                {
                    job.DocumentId = document.Id;
                    job.UpdatedAt = DateTime.UtcNow;
                }
            }

            // Log activity
            _context.ImportReviewActivities.Add(new ImportReviewActivity
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = userId,
                Action = "session_finalized",
                Details = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    documentId = document.Id,
                    importedBlocks = approvedBlocks.Count,
                    skippedBlocks = rejectedCount
                })),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "[ImportReview] Session {SessionId} finalized — document {DocumentId} created with {BlockCount} blocks ({Skipped} skipped)",
                sessionId, document.Id, approvedBlocks.Count, rejectedCount);

            return new FinalizeResultDto(
                new FinalizedDocumentDto(document.Id, document.Title),
                new FinalizeStatisticsDto(approvedBlocks.Count, rejectedCount)
            );
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "[ImportReview] Failed to finalize session {SessionId}", sessionId);
            throw;
        }
    }

    // --- Block Operations ---

    public async Task<BlockReviewDto?> UpdateBlockReviewAsync(Guid sessionId, string blockId, string userId, UpdateBlockReviewDto dto)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        var role = GetUserRole(session, userId);
        if (role == null || role == "viewer") return null;

        var review = await _context.ImportBlockReviews
            .Include(br => br.Reviewer)
            .FirstOrDefaultAsync(br => br.SessionId == sessionId && br.BlockId == blockId);

        if (review == null) return null;

        if (dto.Status != null)
        {
            review.Status = dto.Status;
            review.ReviewedBy = userId;
            review.ReviewedAt = DateTime.UtcNow;
        }

        if (dto.CurrentContent.HasValue)
        {
            review.CurrentContent = JsonDocument.Parse(dto.CurrentContent.Value.GetRawText());
            // If content changed, mark as edited unless explicitly setting another status
            if (dto.Status == null)
            {
                review.Status = "edited";
            }
            review.ReviewedBy = userId;
            review.ReviewedAt = DateTime.UtcNow;
        }

        if (dto.CurrentType != null)
        {
            review.CurrentType = dto.CurrentType;
            review.ReviewedBy = userId;
            review.ReviewedAt = DateTime.UtcNow;
        }

        session.UpdatedAt = DateTime.UtcNow;

        // Log activity
        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = "block_updated",
            BlockId = blockId,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new { status = review.Status })),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        // Reload reviewer for DTO
        if (review.ReviewedBy != null)
        {
            await _context.Entry(review).Reference(r => r.Reviewer).LoadAsync();
        }

        var commentCounts = await GetBlockCommentCounts(sessionId, blockId);
        return MapBlockReviewToDto(review, commentCounts.total, commentCounts.unresolved);
    }

    public async Task<BlockReviewDto?> ResetBlockAsync(Guid sessionId, string blockId, string userId)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        var role = GetUserRole(session, userId);
        if (role == null || role == "viewer") return null;

        var review = await _context.ImportBlockReviews
            .FirstOrDefaultAsync(br => br.SessionId == sessionId && br.BlockId == blockId);

        if (review == null) return null;

        review.Status = "pending";
        review.ReviewedBy = null;
        review.ReviewedAt = null;
        review.CurrentContent = null;
        review.CurrentType = null;

        session.UpdatedAt = DateTime.UtcNow;

        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = "block_reset",
            BlockId = blockId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        var commentCounts = await GetBlockCommentCounts(sessionId, blockId);
        return MapBlockReviewToDto(review, commentCounts.total, commentCounts.unresolved);
    }

    public async Task<int> BulkActionAsync(Guid sessionId, string userId, BulkActionDto dto)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return 0;

        var role = GetUserRole(session, userId);
        if (role == null || role == "viewer") return 0;

        var reviews = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId)
            .ToListAsync();

        var affected = 0;

        switch (dto.Action)
        {
            case "approveAll":
                foreach (var r in reviews.Where(r => r.Status == "pending"))
                {
                    r.Status = "approved";
                    r.ReviewedBy = userId;
                    r.ReviewedAt = DateTime.UtcNow;
                    affected++;
                }
                break;

            case "rejectErrors":
                foreach (var r in reviews.Where(r => r.Warnings != null && r.Status == "pending"))
                {
                    // Check if warnings array is non-empty
                    if (r.Warnings!.RootElement.ValueKind == JsonValueKind.Array && r.Warnings.RootElement.GetArrayLength() > 0)
                    {
                        r.Status = "rejected";
                        r.ReviewedBy = userId;
                        r.ReviewedAt = DateTime.UtcNow;
                        affected++;
                    }
                }
                break;

            case "approveHighConfidence":
                foreach (var r in reviews.Where(r => r.Status == "pending" && r.Confidence.HasValue && r.Confidence.Value >= 80))
                {
                    r.Status = "approved";
                    r.ReviewedBy = userId;
                    r.ReviewedAt = DateTime.UtcNow;
                    affected++;
                }
                break;

            case "resetAll":
                foreach (var r in reviews.Where(r => r.Status != "pending"))
                {
                    r.Status = "pending";
                    r.ReviewedBy = null;
                    r.ReviewedAt = null;
                    r.CurrentContent = null;
                    r.CurrentType = null;
                    affected++;
                }
                break;

            case "approveSelected":
                if (dto.BlockIds != null)
                {
                    var selectedIds = new HashSet<string>(dto.BlockIds);
                    foreach (var r in reviews.Where(r => selectedIds.Contains(r.BlockId) && r.Status == "pending"))
                    {
                        r.Status = "approved";
                        r.ReviewedBy = userId;
                        r.ReviewedAt = DateTime.UtcNow;
                        affected++;
                    }
                }
                break;

            case "rejectSelected":
                if (dto.BlockIds != null)
                {
                    var selectedIds = new HashSet<string>(dto.BlockIds);
                    foreach (var r in reviews.Where(r => selectedIds.Contains(r.BlockId) && r.Status == "pending"))
                    {
                        r.Status = "rejected";
                        r.ReviewedBy = userId;
                        r.ReviewedAt = DateTime.UtcNow;
                        affected++;
                    }
                }
                break;
        }

        if (affected > 0)
        {
            session.UpdatedAt = DateTime.UtcNow;

            _context.ImportReviewActivities.Add(new ImportReviewActivity
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = userId,
                Action = "bulk_action",
                Details = JsonDocument.Parse(JsonSerializer.Serialize(new { action = dto.Action, affected })),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("[ImportReview] Bulk action '{Action}' on session {SessionId} affected {Count} blocks",
            dto.Action, sessionId, affected);

        return affected;
    }

    // --- Collaborators ---

    public async Task<CollaboratorInfoDto?> AddCollaboratorAsync(Guid sessionId, string userId, AddReviewCollaboratorDto dto)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        var role = GetUserRole(session, userId);
        if (role != "owner") return null;

        // Find user by email
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (targetUser == null) return null;

        // Check if already a collaborator
        if (session.Collaborators.Any(c => c.UserId == targetUser.Id)) return null;

        var collaborator = new ImportReviewCollaborator
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = targetUser.Id,
            Role = dto.Role,
            InvitedBy = userId,
            InvitedAt = DateTime.UtcNow
        };

        _context.ImportReviewCollaborators.Add(collaborator);

        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = "collaborator_added",
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new { targetUserId = targetUser.Id, role = dto.Role })),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return new CollaboratorInfoDto(
            targetUser.Id,
            dto.Role,
            collaborator.InvitedAt,
            collaborator.LastActiveAt,
            new ReviewUserWithEmailDto(targetUser.Id, targetUser.Name, targetUser.Email, targetUser.Image)
        );
    }

    public async Task<bool> RemoveCollaboratorAsync(Guid sessionId, string targetUserId, string userId)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        var role = GetUserRole(session, userId);
        if (role != "owner") return false;

        var collaborator = session.Collaborators.FirstOrDefault(c => c.UserId == targetUserId);
        if (collaborator == null || collaborator.Role == "owner") return false;

        _context.ImportReviewCollaborators.Remove(collaborator);

        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = "collaborator_removed",
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new { targetUserId })),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return true;
    }

    // --- Comments ---

    public async Task<ReviewCommentDto?> AddCommentAsync(Guid sessionId, string userId, AddReviewCommentDto dto)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        var role = GetUserRole(session, userId);
        if (role == null) return null;

        var comment = new ImportBlockComment
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            BlockId = dto.BlockId,
            UserId = userId,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.ImportBlockComments.Add(comment);

        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = "comment_added",
            BlockId = dto.BlockId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(userId);

        return new ReviewCommentDto(
            comment.Id,
            comment.BlockId,
            comment.Content,
            comment.Resolved,
            comment.ResolvedAt,
            comment.CreatedAt,
            comment.UpdatedAt,
            new ReviewUserDto(userId, user?.Name, user?.Image),
            null
        );
    }

    public async Task<bool> UpdateCommentAsync(Guid sessionId, Guid commentId, string userId, UpdateReviewCommentDto dto)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        var role = GetUserRole(session, userId);
        if (role == null) return false;

        var comment = await _context.ImportBlockComments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.SessionId == sessionId);

        if (comment == null) return false;

        comment.Resolved = dto.Resolved;
        comment.UpdatedAt = DateTime.UtcNow;

        if (dto.Resolved)
        {
            comment.ResolvedBy = userId;
            comment.ResolvedAt = DateTime.UtcNow;
        }
        else
        {
            comment.ResolvedBy = null;
            comment.ResolvedAt = null;
        }

        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = dto.Resolved ? "comment_resolved" : "comment_unresolved",
            BlockId = comment.BlockId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCommentAsync(Guid sessionId, Guid commentId, string userId)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        var role = GetUserRole(session, userId);
        if (role == null) return false;

        var comment = await _context.ImportBlockComments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.SessionId == sessionId);

        if (comment == null) return false;

        // Only the comment author or the session owner can delete
        if (comment.UserId != userId && role != "owner") return false;

        _context.ImportBlockComments.Remove(comment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<ReviewCommentDto>> GetCommentsAsync(Guid sessionId, string userId, string? blockId = null)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return [];

        var role = GetUserRole(session, userId);
        if (role == null) return [];

        var query = _context.ImportBlockComments
            .Include(c => c.User)
            .Include(c => c.Resolver)
            .Where(c => c.SessionId == sessionId);

        if (!string.IsNullOrEmpty(blockId))
        {
            query = query.Where(c => c.BlockId == blockId);
        }

        var comments = await query
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        return comments.Select(c => new ReviewCommentDto(
            c.Id,
            c.BlockId,
            c.Content,
            c.Resolved,
            c.ResolvedAt,
            c.CreatedAt,
            c.UpdatedAt,
            new ReviewUserDto(c.User.Id, c.User.Name, c.User.Image),
            c.Resolver != null ? new ReviewUserDto(c.Resolver.Id, c.Resolver.Name, null) : null
        )).ToList();
    }

    // --- Activity ---

    public async Task<List<ReviewActivityDto>> GetActivitiesAsync(Guid sessionId, string userId, int limit = 50)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return [];

        var role = GetUserRole(session, userId);
        if (role == null) return [];

        var activities = await _context.ImportReviewActivities
            .Include(a => a.User)
            .Where(a => a.SessionId == sessionId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return activities.Select(MapActivityToDto).ToList();
    }

    public async Task<List<ReviewActivityDto>> GetRecentActivitiesAsync(Guid sessionId, string userId, DateTime since)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return [];

        var role = GetUserRole(session, userId);
        if (role == null) return [];

        var activities = await _context.ImportReviewActivities
            .Include(a => a.User)
            .Where(a => a.SessionId == sessionId && a.CreatedAt > since)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return activities.Select(MapActivityToDto).ToList();
    }

    // --- Auto-creation from import ---

    public async Task<CreateSessionResponseDto> CreateSessionFromImportAsync(
        string userId,
        Guid jobId,
        string documentTitle,
        List<CreateReviewBlockDto> blocks,
        List<object>? warnings = null)
    {
        var warningsJson = warnings != null
            ? JsonSerializer.SerializeToElement(warnings)
            : (JsonElement?)null;

        var dto = new CreateReviewSessionDto(
            JobId: jobId,
            DocumentTitle: documentTitle,
            Blocks: blocks,
            Warnings: warningsJson
        );

        return await CreateSessionAsync(userId, dto);
    }

    // --- Private Helpers ---

    private static string? GetUserRole(ImportReviewSession session, string userId)
    {
        // Owner always has access
        if (session.OwnerId == userId) return "owner";

        var collaborator = session.Collaborators.FirstOrDefault(c => c.UserId == userId);
        return collaborator?.Role;
    }

    private async Task<(int total, int unresolved)> GetBlockCommentCounts(Guid sessionId, string blockId)
    {
        var total = await _context.ImportBlockComments
            .CountAsync(c => c.SessionId == sessionId && c.BlockId == blockId);
        var unresolved = await _context.ImportBlockComments
            .CountAsync(c => c.SessionId == sessionId && c.BlockId == blockId && !c.Resolved);
        return (total, unresolved);
    }

    private static ReviewSessionInfoDto MapSessionToDto(ImportReviewSession session)
    {
        return new ReviewSessionInfoDto(
            session.Id,
            session.JobId,
            session.OwnerId,
            session.DocumentTitle,
            session.Status,
            session.CreatedAt,
            session.UpdatedAt,
            session.ExpiresAt,
            session.DocumentId,
            session.OriginalWarnings != null
                ? JsonSerializer.Deserialize<JsonElement>(session.OriginalWarnings.RootElement.GetRawText())
                : null
        );
    }

    private static BlockReviewDto MapBlockReviewToDto(ImportBlockReview br, int commentCount, int unresolvedCommentCount)
    {
        ReviewUserDto? reviewerDto = null;
        if (br.Reviewer != null)
        {
            reviewerDto = new ReviewUserDto(br.Reviewer.Id, br.Reviewer.Name, br.Reviewer.Image);
        }

        return new BlockReviewDto(
            br.Id,
            br.BlockId,
            br.BlockIndex,
            br.Status,
            br.ReviewedBy,
            br.ReviewedAt,
            reviewerDto,
            JsonSerializer.Deserialize<JsonElement>(br.OriginalContent.RootElement.GetRawText()),
            br.OriginalType,
            br.CurrentContent != null
                ? JsonSerializer.Deserialize<JsonElement>(br.CurrentContent.RootElement.GetRawText())
                : null,
            br.CurrentType,
            br.Confidence,
            br.Warnings != null
                ? JsonSerializer.Deserialize<JsonElement>(br.Warnings.RootElement.GetRawText())
                : null,
            br.SortOrder,
            br.Depth,
            commentCount,
            unresolvedCommentCount
        );
    }

    private static ReviewActivityDto MapActivityToDto(ImportReviewActivity a)
    {
        return new ReviewActivityDto(
            a.Id,
            a.Action,
            a.BlockId,
            a.Details != null
                ? JsonSerializer.Deserialize<JsonElement>(a.Details.RootElement.GetRawText())
                : null,
            a.CreatedAt,
            new ReviewUserDto(a.User.Id, a.User.Name, a.User.Image)
        );
    }
}
