using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Lilia.Api.Services;

public class ImportReviewMessages { }

public class ImportReviewService : IImportReviewService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<ImportReviewService> _logger;
    private readonly IStringLocalizer<ImportReviewMessages> _localizer;

    private readonly IRenderService? _renderService;
    private readonly IStorageService? _storageService;
    private readonly ILatexProjectExtractor? _projectExtractor;

    public ImportReviewService(
        LiliaDbContext context,
        ILogger<ImportReviewService> logger,
        IStringLocalizer<ImportReviewMessages> localizer,
        IRenderService? renderService = null,
        IStorageService? storageService = null,
        ILatexProjectExtractor? projectExtractor = null)
    {
        _context = context;
        _logger = logger;
        _localizer = localizer;
        _renderService = renderService;
        _storageService = storageService;
        _projectExtractor = projectExtractor;
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

        // Auto-insert a tableOfContents block when ≥2 headings are detected and no TOC exists
        var headingCount = blockReviews.Count(b =>
            b.OriginalType == BlockTypes.Heading || b.OriginalType == "header");
        var hasTocBlock = blockReviews.Any(b =>
            b.OriginalType == BlockTypes.TableOfContents || b.OriginalType == "toc");

        if (headingCount >= 2 && !hasTocBlock)
        {
            var tocWarningMessage = _localizer["AutoTocWarning"].Value;
            var tocWarnings = JsonDocument.Parse(
                $"[{{\"id\":\"auto-toc-warning\",\"type\":\"AutoInsertedToc\",\"message\":\"{tocWarningMessage}\",\"severity\":\"info\"}}]");

            blockReviews.Insert(0, new ImportBlockReview
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                BlockIndex = -1,
                BlockId = Guid.NewGuid().ToString(),
                Status = "pending",
                OriginalContent = JsonDocument.Parse("{}"),
                OriginalType = BlockTypes.TableOfContents,
                Confidence = 100,
                Warnings = tocWarnings,
                SortOrder = -1,
                Depth = 0
            });
        }

        // Detect and flag clusters of consecutive headings that look like imported TOC entries.
        // A run of ≥5 consecutive heading blocks (no other types in between) is almost certainly
        // a TOC parsed as individual headings by the OCR/import engine.
        FlagTocHeadingClusters(blockReviews);

        // Correct heading levels based on section numbering in the heading text.
        // Mathpix often assigns wrong levels — derive from numbering depth instead:
        //   "Chapter N" / unnumbered front matter → h1
        //   "1.1 Title" → h2, "2.5.1 Title" → h3, etc.
        CorrectHeadingLevelsFromNumbering(blockReviews);

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
        // Permission + state gate. Lightweight projection only — no blocks loaded.
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return null;
        if (GetUserRole(session, userId) != "owner") return null;

        // FT-IMP-001 stage 8 — idempotent checkout. If the instance already
        // produced a document (retry after network blip, refresh that sees
        // a stale state, etc.), short-circuit to the existing document
        // instead of creating a duplicate. The `imported` status + non-null
        // DocumentId combo is our "already done" signal.
        if (session.Status == "imported" && session.DocumentId.HasValue)
        {
            var existingDoc = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == session.DocumentId.Value);
            if (existingDoc != null)
            {
                var importedBlocks = await _context.Blocks.CountAsync(b => b.DocumentId == existingDoc.Id);
                return new FinalizeResultDto(
                    new FinalizedDocumentDto(existingDoc.Id, existingDoc.Title),
                    new FinalizeStatisticsDto(ImportedBlocks: importedBlocks, SkippedBlocks: 0));
            }
            // Document row was purged but session still says imported —
            // fall through and let FinalizeInternalAsync re-create it.
        }

        // The new staging flow transitions to pending_review (after auto-check)
        // or auto_finalized; the legacy review-UI flow still uses in_progress.
        // Accept any of those.
        if (session.Status != "in_progress" && session.Status != "pending_review") return null;

        var pendingCount = await _context.ImportBlockReviews
            .CountAsync(b => b.SessionId == sessionId && b.Status == "pending");
        if (pendingCount > 0 && !dto.Force) return null; // Controller returns 400

        var documentTitle = dto.DocumentTitle ?? session.DocumentTitle;
        return await FinalizeInternalAsync(sessionId, userId, documentTitle, dto.Force, CancellationToken.None);
    }

    public Task<FinalizeResultDto> FinalizeFromStagingAsync(
        Guid sessionId, string ownerId, string documentTitle, bool force, CancellationToken ct = default)
    {
        // Trusted entrypoint — LatexImportJobExecutor already validated that the
        // session is clean and that the owner matches. Skip the permission +
        // status checks to avoid loading the session twice.
        return FinalizeInternalAsync(sessionId, ownerId, documentTitle, force, ct);
    }

    // Core finalize — DB-first. All the per-block copy happens inside a single
    // INSERT INTO blocks ... SELECT FROM import_block_reviews, so the review
    // rows never visit .NET memory even for a 5 000-block thesis. See plan
    // §4 and lilia-docs/docs/guidelines/import-export-db-first.md.
    private async Task<FinalizeResultDto> FinalizeInternalAsync(
        Guid sessionId, string ownerId, string documentTitle, bool force, CancellationToken ct)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // If force=true, promote pending → approved via a single UPDATE.
            // Staging realm never round-trips through app memory.
            if (force)
            {
                await _context.ImportBlockReviews
                    .Where(b => b.SessionId == sessionId && b.Status == "pending")
                    .ExecuteUpdateAsync(b => b
                        .SetProperty(x => x.Status, "approved")
                        .SetProperty(x => x.ReviewedBy, ownerId)
                        .SetProperty(x => x.ReviewedAt, DateTime.UtcNow), ct);
            }

            var documentId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Create the document row first — cheap, one INSERT.
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO documents (id, owner_id, title, created_at, updated_at)
                VALUES ({documentId}, {ownerId}, {documentTitle}, {now}, {now})", ct);

            // The centrepiece: copy approved/edited staged rows into real blocks
            // without routing through the app layer. COALESCE picks edited
            // content over original when the reviewer made changes.
            //
            // 2026-04-24: changed from gen_random_uuid() to reusing
            // import_block_reviews.id as the block id. This gives us a
            // deterministic mapping from import BlockId (string) → new
            // block.Id (guid) via `ibr.block_id → ibr.id → blocks.id`,
            // which the comments-transfer INSERT below relies on to
            // persist review comments onto the finalized document.
            var importedCount = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO blocks (id, document_id, type, content, sort_order, depth, created_at, updated_at)
                SELECT
                    id,
                    {documentId},
                    COALESCE(current_type, original_type),
                    COALESCE(current_content, original_content),
                    sort_order,
                    depth,
                    {now},
                    {now}
                FROM import_block_reviews
                WHERE session_id = {sessionId}
                  AND status IN ('approved','edited')
                ORDER BY sort_order", ct);

            // Transfer review comments onto the finalized document's blocks.
            // Mapping: ImportBlockComment.BlockId (string) joins to
            // ImportBlockReview.BlockId (string); the reused id from above
            // becomes the Comment.BlockId (guid) on the real document.
            // Skipped for rejected/deleted blocks — their comments are
            // discarded along with the block.
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO comments (id, document_id, block_id, user_id, content, resolved, created_at, updated_at)
                SELECT
                    gen_random_uuid(),
                    {documentId},
                    ibr.id,
                    ibc.user_id,
                    ibc.content,
                    ibc.resolved,
                    {now},
                    {now}
                FROM import_block_comments ibc
                JOIN import_block_reviews ibr
                    ON ibr.session_id = ibc.session_id AND ibr.block_id = ibc.block_id
                WHERE ibc.session_id = {sessionId}
                    AND ibr.status IN ('approved','edited')", ct);

            var rejectedCount = await _context.ImportBlockReviews
                .CountAsync(b => b.SessionId == sessionId && b.Status == "rejected", ct);

            // Stage Overleaf zip assets: if the upload was a .zip project,
            // the raw zip is preserved at uploads/imports/{jobId}.zip.
            // Re-extract here, upload every non-.tex file to R2 as an
            // Asset row on the new document, and wire the three block-
            // level categories that can be linked: images → figure.src,
            // .bib → BibliographyEntry, .py/.js/etc → code block content
            // when referenced by \lstinputlisting.
            try
            {
                await StageZipAssetsAsync(sessionId, documentId, ownerId, now, ct);
            }
            catch (Exception ex)
            {
                // Asset staging is non-fatal — the document is created
                // either way, just without the extra files.
                _logger.LogWarning(ex, "[ImportReview] Failed to stage zip assets for session {SessionId}", sessionId);
            }

            // Mark session as imported and link the new document.
            await _context.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, "imported")
                    .SetProperty(x => x.DocumentId, documentId)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            // Link the job (if any). Keeps the job row pointing at the final doc.
            await _context.Jobs
                .Where(j => _context.ImportReviewSessions
                    .Where(s => s.Id == sessionId)
                    .Select(s => s.JobId)
                    .Contains(j.Id))
                .ExecuteUpdateAsync(j => j
                    .SetProperty(x => x.DocumentId, documentId)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            // Activity log. One row, tiny payload — safe to do via EF for the
            // audit trail (atomic with the rest of this transaction).
            _context.ImportReviewActivities.Add(new ImportReviewActivity
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = ownerId,
                Action = "session_finalized",
                Details = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    documentId,
                    importedBlocks = importedCount,
                    skippedBlocks = rejectedCount
                })),
                CreatedAt = now
            });
            await _context.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[ImportReview] Session {SessionId} finalized — document {DocumentId} created with {BlockCount} blocks ({Skipped} skipped)",
                sessionId, documentId, importedCount, rejectedCount);

            return new FinalizeResultDto(
                new FinalizedDocumentDto(documentId, documentTitle),
                new FinalizeStatisticsDto(importedCount, rejectedCount)
            );
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "[ImportReview] Failed to finalize session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<List<ImportDiagnosticDto>> GetDiagnosticsAsync(Guid sessionId, string userId)
    {
        // Permission gate — use the existing role check.
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return new();
        if (GetUserRole(session, userId) is null) return new();

        return await _context.ImportDiagnostics
            .Where(d => d.SessionId == sessionId)
            .OrderBy(d => d.Severity == "error" ? 0 : d.Severity == "warning" ? 1 : 2)
            .ThenBy(d => d.SourceLineStart ?? int.MaxValue)
            .Select(d => new ImportDiagnosticDto(
                d.Id, d.SessionId, d.BlockId, d.ElementPath,
                d.SourceLineStart, d.SourceLineEnd, d.SourceColStart, d.SourceColEnd,
                d.SourceSnippet, d.Category, d.Severity, d.Code, d.Message,
                d.SuggestedAction, d.AutoFixApplied, d.DocsUrl,
                d.Dismissed, d.DismissedBy, d.DismissedAt, d.CreatedAt))
            .ToListAsync();
    }

    public async Task<bool> SetSessionCategoryAsync(Guid sessionId, string userId, string? category)
    {
        // Only owner can change category — categories drive downstream
        // finding rules so we gate on write.
        var affected = await _context.ImportReviewSessions
            .Where(s => s.Id == sessionId && s.OwnerId == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.DocumentCategory, category)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        return affected > 0;
    }

    public async Task<ImportDiagnosticDto?> DismissDiagnosticAsync(Guid sessionId, Guid diagnosticId, string userId)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return null;
        var role = GetUserRole(session, userId);
        if (role != "owner" && role != "reviewer") return null;

        var now = DateTime.UtcNow;
        var updated = await _context.ImportDiagnostics
            .Where(d => d.Id == diagnosticId && d.SessionId == sessionId)
            .ExecuteUpdateAsync(d => d
                .SetProperty(x => x.Dismissed, true)
                .SetProperty(x => x.DismissedBy, userId)
                .SetProperty(x => x.DismissedAt, now));
        if (updated == 0) return null;

        return await _context.ImportDiagnostics
            .Where(d => d.Id == diagnosticId)
            .Select(d => new ImportDiagnosticDto(
                d.Id, d.SessionId, d.BlockId, d.ElementPath,
                d.SourceLineStart, d.SourceLineEnd, d.SourceColStart, d.SourceColEnd,
                d.SourceSnippet, d.Category, d.Severity, d.Code, d.Message,
                d.SuggestedAction, d.AutoFixApplied, d.DocsUrl,
                d.Dismissed, d.DismissedBy, d.DismissedAt, d.CreatedAt))
            .FirstOrDefaultAsync();
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

        // SortOrder write-through. Clients use this for drag-reorder from
        // the Studio-parity review page. No rebalancing here — callers
        // choose integer or fractional orderings as they see fit. A future
        // bulk-reorder endpoint can renumber if sortOrders get sparse.
        if (dto.SortOrder.HasValue)
        {
            review.SortOrder = dto.SortOrder.Value;
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
        List<object>? warnings = null,
        JsonElement? paragraphTraces = null,
        string? sourceFilePath = null,
        string? rawImportData = null)
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

        var result = await CreateSessionAsync(userId, dto);

        // Store paragraph traces and source file path if provided
        if (paragraphTraces.HasValue || sourceFilePath != null || rawImportData != null)
        {
            var session = await _context.ImportReviewSessions.FindAsync(result.Session.Id);
            if (session != null)
            {
                if (paragraphTraces.HasValue)
                {
                    session.ParagraphTraces = JsonDocument.Parse(paragraphTraces.Value.GetRawText());
                }
                session.SourceFilePath = sourceFilePath;
                session.RawImportData = rawImportData;
                session.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        return result;
    }

    public async Task<JsonElement?> GetParagraphTracesAsync(Guid sessionId, string userId)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return null;

        // Check access
        var role = GetUserRole(session, userId);
        if (role == null) return null;

        if (session.ParagraphTraces == null) return null;

        return JsonSerializer.Deserialize<JsonElement>(session.ParagraphTraces.RootElement.GetRawText());
    }

    // --- Private Helpers ---

    /// <summary>
    /// Detects clusters of consecutive heading blocks that are likely imported TOC entries
    /// (e.g. from Mathpix OCR) and flags them as rejected with an info warning.
    /// A run of ≥5 consecutive headings with no other block types in between is flagged.
    /// </summary>
    private void FlagTocHeadingClusters(List<ImportBlockReview> blockReviews)
    {
        const int minClusterSize = 5;
        var headingTypes = new HashSet<string> { BlockTypes.Heading, "header" };

        var runStart = -1;
        var runLength = 0;

        for (var i = 0; i <= blockReviews.Count; i++)
        {
            var isHeading = i < blockReviews.Count && headingTypes.Contains(blockReviews[i].OriginalType);

            if (isHeading)
            {
                if (runStart == -1) runStart = i;
                runLength++;
            }
            else
            {
                // End of a run — flag if long enough
                if (runLength >= minClusterSize)
                {
                    for (var j = runStart; j < runStart + runLength; j++)
                    {
                        var block = blockReviews[j];
                        block.Status = "rejected";

                        // Append a warning to existing warnings
                        var warningId = Guid.NewGuid().ToString();
                        var warningJson = "[{\"id\":\"" + warningId + "\",\"type\":\"PossibleTocEntry\",\"message\":\"" + _localizer["TocEntryWarning"].Value.Replace("\"", "\\\"") + "\",\"severity\":\"info\"}]";

                        if (block.Warnings != null)
                        {
                            // Merge with existing warnings array
                            var existing = block.Warnings.RootElement.EnumerateArray().Select(e => e.GetRawText());
                            var newWarning = JsonDocument.Parse(warningJson).RootElement.EnumerateArray().Select(e => e.GetRawText());
                            var merged = "[" + string.Join(",", existing.Concat(newWarning)) + "]";
                            block.Warnings = JsonDocument.Parse(merged);
                        }
                        else
                        {
                            block.Warnings = JsonDocument.Parse(warningJson);
                        }
                    }
                }

                runStart = -1;
                runLength = 0;
            }
        }
    }

    /// <summary>
    /// Corrects heading levels based on section numbering in the heading text.
    /// Mathpix/OCR importers often assign wrong levels. This derives the correct level
    /// from the numbering pattern in the text:
    ///   "Chapter N" / front matter (Declaration, Abstract, etc.) → h1
    ///   "N Title" (e.g. "1 Introduction") → h1
    ///   "N.N Title" (e.g. "1.1 Email Communication") → h2
    ///   "N.N.N Title" (e.g. "2.5.1 Tokenization") → h3
    ///   Unnumbered headings without a known pattern → left unchanged
    /// </summary>
    private void CorrectHeadingLevelsFromNumbering(List<ImportBlockReview> blockReviews)
    {
        var headingTypes = new HashSet<string> { BlockTypes.Heading, "header" };

        // Known front-matter / top-level heading patterns (unnumbered)
        var topLevelPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "abstract", "acknowledgements", "acknowledgment", "declaration",
            "certificate", "dedication", "preface", "foreword",
            "list of figures", "list of tables", "list of abbreviations",
            "table of contents", "contents", "bibliography", "references",
            "appendix", "glossary", "index", "conclusion", "summary"
        };

        ImportBlockReview? previousHeading = null;

        for (var i = 0; i < blockReviews.Count; i++)
        {
            var block = blockReviews[i];
            if (!headingTypes.Contains(block.OriginalType)) continue;

            var text = ExtractTextFromContent(block.OriginalContent);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var trimmed = text.Trim();
            int? correctLevel = null;

            // Rule 1: "Chapter N" → reject (it's a label, not content).
            // The actual chapter title follows as the next heading.
            if (Regex.IsMatch(trimmed, @"^Chapter\s+\d+\s*$", RegexOptions.IgnoreCase))
            {
                block.Status = "rejected";
                var warningId = Guid.NewGuid().ToString();
                var warningJson = "[{\"id\":\"" + warningId + "\",\"type\":\"ChapterLabel\",\"message\":\"" + _localizer["ChapterLabelWarning"].Value.Replace("\"", "\\\"") + "\",\"severity\":\"info\"}]";
                block.Warnings = block.Warnings != null
                    ? JsonDocument.Parse("[" + string.Join(",",
                        block.Warnings.RootElement.EnumerateArray().Select(e => e.GetRawText())
                        .Concat(JsonDocument.Parse(warningJson).RootElement.EnumerateArray().Select(e => e.GetRawText()))) + "]")
                    : JsonDocument.Parse(warningJson);
                previousHeading = block;
                continue;
            }

            // Rule 2: Numbered heading "N.N.N Title" → derive level from dot count
            var numberedMatch = Regex.Match(trimmed, @"^(\d+(?:\.\d+)*)\s");
            if (numberedMatch.Success)
            {
                var number = numberedMatch.Groups[1].Value;
                var dotCount = number.Count(c => c == '.');
                correctLevel = dotCount + 1;
            }

            // Rule 3: Known front-matter / top-level titles → h1
            if (correctLevel == null && topLevelPatterns.Contains(trimmed))
            {
                correctLevel = 1;
            }

            // Rule 4: Heading immediately after a rejected "Chapter N" (no non-heading blocks between) → h1
            // This is the actual chapter title (e.g., "Introduction" after "Chapter 1")
            if (correctLevel == null && previousHeading != null && previousHeading.Status == "rejected")
            {
                var prevText = ExtractTextFromContent(previousHeading.OriginalContent)?.Trim() ?? "";
                if (Regex.IsMatch(prevText, @"^Chapter\s+\d+\s*$", RegexOptions.IgnoreCase))
                {
                    var hasNonHeadingBetween = false;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        if (blockReviews[j] == previousHeading) break;
                        if (!headingTypes.Contains(blockReviews[j].OriginalType))
                        {
                            hasNonHeadingBetween = true;
                            break;
                        }
                    }

                    if (!hasNonHeadingBetween)
                    {
                        correctLevel = 1;
                    }
                }
            }

            if (correctLevel != null)
            {
                correctLevel = Math.Clamp(correctLevel.Value, 1, 6);
                UpdateHeadingLevel(block, correctLevel.Value);
            }

            // Strip leading numbering from heading text (e.g., "1.1. Title" → "Title")
            // so the ToC doesn't show double numbering (auto-generated + embedded).
            if (numberedMatch.Success)
            {
                StripLeadingNumbering(block, numberedMatch.Value);
            }

            previousHeading = block;
        }
    }

    /// <summary>
    /// Extracts plain text from a block's JSON content.
    /// Tries common field names: text, html, value, content.
    /// </summary>
    private static string ExtractTextFromContent(JsonDocument? content)
    {
        if (content == null) return "";

        var root = content.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return "";

        // Try "text" field first (most common for headings)
        if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            return textProp.GetString() ?? "";

        if (root.TryGetProperty("html", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String)
            return htmlProp.GetString() ?? "";

        if (root.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.String)
            return valueProp.GetString() ?? "";

        if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            return contentProp.GetString() ?? "";

        return "";
    }


    /// <summary>
    /// Updates the heading level in a block's content JSON.
    /// Sets CurrentContent with the corrected level, marking the block as edited.
    /// </summary>
    private static void UpdateHeadingLevel(ImportBlockReview block, int level)
    {
        var source = block.CurrentContent ?? block.OriginalContent;
        if (source == null) return;

        var root = source.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        // Check current level — skip if already correct
        if (root.TryGetProperty("level", out var currentLevel) &&
            currentLevel.ValueKind == JsonValueKind.Number &&
            currentLevel.GetInt32() == level)
        {
            return;
        }

        // Build updated JSON with corrected level
        var dict = new Dictionary<string, object?>();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "level")
            {
                dict["level"] = level;
            }
            else
            {
                dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
            }
        }

        // Ensure level is present even if it wasn't before
        if (!dict.ContainsKey("level"))
        {
            dict["level"] = level;
        }

        block.CurrentContent = JsonDocument.Parse(JsonSerializer.Serialize(dict));
        block.CurrentType ??= block.OriginalType; // preserve type if not already changed
    }

    /// <summary>
    /// Strips leading numbering prefix from a heading's text/html content.
    /// E.g., "1.1. problematique formulation" → "problematique formulation"
    /// Prevents double numbering when the ToC generates its own numbers.
    /// </summary>
    private static void StripLeadingNumbering(ImportBlockReview block, string matchedPrefix)
    {
        var source = block.CurrentContent ?? block.OriginalContent;
        if (source == null) return;

        var root = source.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        // Pattern: strip "1.2.3. " or "1.2.3 " (numbering + optional dot + space)
        var stripPattern = @"^" + Regex.Escape(matchedPrefix.TrimEnd()) + @"\.?\s*";

        var dict = new Dictionary<string, object?>();
        var changed = false;

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name is "text" or "html" && prop.Value.ValueKind == JsonValueKind.String)
            {
                var original = prop.Value.GetString() ?? "";
                var stripped = Regex.Replace(original, stripPattern, "").TrimStart();
                if (stripped != original)
                {
                    dict[prop.Name] = stripped;
                    changed = true;
                    continue;
                }
            }
            dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        }

        if (changed)
        {
            block.CurrentContent = JsonDocument.Parse(JsonSerializer.Serialize(dict));
            block.CurrentType ??= block.OriginalType;
        }
    }

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
                : null,
            session.AutoFinalizeEnabled,
            session.QualityScore,
            session.DocumentCategory,
            session.SourceFormat,
            session.LastFocusedTab,
            session.TabProgress != null
                ? JsonSerializer.Deserialize<JsonElement>(session.TabProgress.RootElement.GetRawText())
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

    // --- Reviews in progress dashboard ---

    public async Task<List<ReviewSessionSummaryDto>> ListActiveSessionsAsync(string userId)
    {
        // DB-driven projection — counts computed inline via COUNT FILTER so
        // we don't load block review rows into memory for the badge numbers.
        var rows = await _context.ImportReviewSessions
            .Where(s =>
                s.OwnerId == userId
                && s.Status != "imported"
                && s.Status != "cancelled")
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new
            {
                s.Id,
                s.DocumentTitle,
                s.Status,
                s.CreatedAt,
                s.UpdatedAt,
                s.ExpiresAt,
                TotalBlocks = s.BlockReviews.Count(),
                ApprovedBlocks = s.BlockReviews.Count(br => br.Status == "approved" || br.Status == "edited"),
                RejectedBlocks = s.BlockReviews.Count(br => br.Status == "rejected"),
                PendingBlocks = s.BlockReviews.Count(br => br.Status == "pending"),
            })
            .ToListAsync();

        return rows.Select(r => new ReviewSessionSummaryDto(
            r.Id, r.DocumentTitle, r.Status,
            r.TotalBlocks, r.ApprovedBlocks, r.RejectedBlocks, r.PendingBlocks,
            r.CreatedAt, r.UpdatedAt, r.ExpiresAt
        )).ToList();
    }

    // --- Tier 1 bulk-convert ---

    // DB-driven bulk-convert against import_block_reviews. Pattern mirrors
    // BlockService.BatchConvertAsync — one tiny type projection for the
    // heuristic, one UPDATE that reads current_content / original_content
    // via coalesce in SQL (never transits .NET memory), one bulk DELETE.
    public async Task<SessionTreeDto?> GetSessionTreeAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return null;
        if (GetUserRole(session, userId) == null) return null;

        // Pull every block in order. We need the JSONB content to extract
        // heading level + preview text, but we read only the shape needed
        // (projected, no entity tracking) to keep this cheap.
        var rows = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId)
            .OrderBy(br => br.SortOrder)
            .Select(br => new
            {
                br.BlockId,
                Type = br.CurrentType ?? br.OriginalType,
                Content = br.CurrentContent ?? br.OriginalContent,
                br.Status,
                br.Depth,
            })
            .ToListAsync(ct);

        // Build the outline tree: headings own the blocks that follow
        // them at deeper levels; non-heading leaves attach to the nearest
        // enclosing heading.
        var roots = new List<SessionTreeNodeDto>();
        var stack = new Stack<(SessionTreeNodeDto Node, int Level, List<SessionTreeNodeDto> Children)>();

        foreach (var r in rows)
        {
            var headingLevel = ReadHeadingLevel(r.Content);
            var preview = ReadPreviewText(r.Content);
            var node = new SessionTreeNodeDto(
                BlockId: r.BlockId,
                Type: r.Type,
                Text: preview,
                HeadingLevel: headingLevel,
                Depth: r.Depth,
                Status: r.Status,
                ChildBlockCount: 1,
                Children: new List<SessionTreeNodeDto>());

            if (headingLevel.HasValue)
            {
                while (stack.Count > 0 && stack.Peek().Level >= headingLevel.Value)
                    stack.Pop();

                if (stack.Count == 0) roots.Add(node);
                else stack.Peek().Children.Add(node);

                stack.Push((node, headingLevel.Value, node.Children));
            }
            else
            {
                if (stack.Count == 0) roots.Add(node);
                else stack.Peek().Children.Add(node);
            }
        }

        // Post-order walk to compute descendant counts.
        int CountDescendants(SessionTreeNodeDto n)
        {
            var total = 1;
            foreach (var c in n.Children) total += CountDescendants(c);
            return total;
        }
        var normalised = roots.Select(r => r with { ChildBlockCount = CountDescendants(r) }).ToList();

        return new SessionTreeDto(sessionId, rows.Count, normalised);
    }

    // Extract a preview string from the block's JSONB content — first
    // populated string field among the common keys. Truncates to 80
    // chars for the tree label.
    private static string? ReadPreviewText(JsonDocument? content)
    {
        if (content == null) return null;
        var root = content.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "text", "title", "caption", "latex", "code", "name" })
        {
            if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = (v.GetString() ?? string.Empty).Trim();
                if (s.Length == 0) continue;
                return s.Length > 80 ? s[..80] + "…" : s;
            }
        }
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
        {
            return "(list of " + items.GetArrayLength() + ")";
        }
        return null;
    }

    private static int? ReadHeadingLevel(JsonDocument? content)
    {
        if (content == null) return null;
        var root = content.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("level", out var lvl) && lvl.TryGetInt32(out var n)) return n;
        return null;
    }

    public async Task<TabStatsDto?> GetTabStatsAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return null;
        if (GetUserRole(session, userId) == null) return null;

        // Bucket all blocks by aspect once — avoids eight round-trips.
        // Named tuple (not anonymous) so the local MakeEntry Func binds.
        var projected = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId)
            .Select(br => new
            {
                Type = br.CurrentType ?? br.OriginalType,
                br.Status,
            })
            .ToListAsync(ct);
        var buckets = projected.Select(p => (Type: p.Type, Status: p.Status)).ToList();

        var diagCount = await _context.ImportDiagnostics.CountAsync(d => d.SessionId == sessionId && !d.Dismissed, ct);
        var diagTotal = await _context.ImportDiagnostics.CountAsync(d => d.SessionId == sessionId, ct);

        var tabProgress = session.TabProgress?.RootElement;
        string ProgressFor(string tab, int pending, int total)
        {
            if (tabProgress?.ValueKind == JsonValueKind.Object
                && tabProgress.Value.TryGetProperty(tab, out var raw)
                && raw.ValueKind == JsonValueKind.String)
            {
                var stored = raw.GetString();
                if (stored == "done") return "done";
                if (stored == "in_progress") return "in_progress";
            }
            if (total == 0) return "unvisited";
            return pending == 0 ? "done" : "unvisited";
        }

        TabStatEntryDto MakeEntry(string tab, Func<(string Type, string Status), bool> filter)
        {
            var rows = buckets.Where(filter).ToList();
            var pending = rows.Count(r => r.Status == "pending");
            var done = rows.Count(r => r.Status != "pending");
            return new TabStatEntryDto(pending, done, rows.Count, ProgressFor(tab, pending, rows.Count));
        }

        var structureTypes = new[] { "heading", "tableOfContents" };
        var contentTypes = new[] { "paragraph", "blockquote", "list", "abstract", "code" };
        var mediaTypes = new[] { "figure", "image" };
        var mathTypes = new[] { "equation", "theorem" };
        var citationTypes = new[] { "bibliography" };

        return new TabStatsDto(
            SessionId: sessionId,
            Structure: MakeEntry("structure", r => structureTypes.Contains(r.Type)),
            Content: MakeEntry("content", r => contentTypes.Contains(r.Type)),
            Tables: MakeEntry("tables", r => r.Type == "table"),
            Media: MakeEntry("media", r => mediaTypes.Contains(r.Type)),
            Math: MakeEntry("math", r => mathTypes.Contains(r.Type)),
            Citations: MakeEntry("citations", r => citationTypes.Contains(r.Type)),
            Coverage: new TabStatEntryDto(
                Pending: 0, Done: 0, Total: 0,
                ProgressState: ProgressFor("coverage", 0, 0)),
            Diagnostics: new TabStatEntryDto(diagCount, diagTotal - diagCount, diagTotal, ProgressFor("diagnostics", diagCount, diagTotal)),
            LastFocusedTab: session.LastFocusedTab);
    }

    public async Task<SessionReportDto?> GetSessionReportAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return null;
        if (GetUserRole(session, userId) == null) return null;

        // One grouped query for block counts by status.
        var byStatus = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId)
            .GroupBy(br => br.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Cnt(string s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
        var blocks = new ReportCountsDto(
            Total: byStatus.Sum(x => x.Count),
            Approved: Cnt("approved"),
            Rejected: Cnt("rejected"),
            Pending: Cnt("pending"),
            Edited: Cnt("edited"));

        var diagBySeverity = await _context.ImportDiagnostics
            .Where(d => d.SessionId == sessionId)
            .GroupBy(d => d.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Sev(string s) => diagBySeverity.FirstOrDefault(x => x.Severity == s)?.Count ?? 0;
        var diagnostics = new ReportCountsDto(
            Total: diagBySeverity.Sum(x => x.Count),
            Approved: 0,
            Rejected: 0,
            Pending: Sev("error") + Sev("warning"),
            Edited: Sev("info"));

        var topUnsupported = await _context.LatexTokenUsages
            .Where(u => u.SessionId == sessionId)
            .Join(_context.LatexTokens.Where(t => t.CoverageLevel == "unsupported" || t.CoverageLevel == "none"),
                u => u.TokenId,
                t => t.Id,
                (u, t) => new { t.Name, t.Kind, t.PackageSlug, u.Count, t.CoverageLevel })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        var totalUsage = await _context.LatexTokenUsages
            .Where(u => u.SessionId == sessionId)
            .SumAsync(u => (int?)u.Count, ct) ?? 0;
        var coveredUsage = await _context.LatexTokenUsages
            .Where(u => u.SessionId == sessionId)
            .Join(_context.LatexTokens.Where(t => t.CoverageLevel == "full" || t.CoverageLevel == "partial" || t.CoverageLevel == "shimmed"),
                u => u.TokenId,
                t => t.Id,
                (u, _) => u.Count)
            .SumAsync(c => (int?)c, ct) ?? 0;
        double? coveragePercent = totalUsage == 0 ? null : Math.Round(100.0 * coveredUsage / totalUsage, 1);

        var activityCount = await _context.ImportReviewActivities.CountAsync(a => a.SessionId == sessionId, ct);

        DateTime? finalizedAt = null;
        double? duration = null;
        if (session.Status == "imported" || session.Status == "cancelled")
        {
            finalizedAt = session.UpdatedAt;
            duration = Math.Round((session.UpdatedAt - session.CreatedAt).TotalMinutes, 1);
        }

        return new SessionReportDto(
            SessionId: sessionId,
            DocumentTitle: session.DocumentTitle,
            Status: session.Status,
            SourceFormat: session.SourceFormat,
            CreatedAt: session.CreatedAt,
            FinalizedAt: finalizedAt,
            DurationMinutes: duration,
            ProducedDocumentId: session.DocumentId,
            Blocks: blocks,
            Diagnostics: diagnostics,
            QualityScore: session.QualityScore,
            CoveragePercent: coveragePercent,
            TopUnsupported: topUnsupported.Select(t => new ReportTokenDto(t.Name, t.Kind, t.PackageSlug, t.Count, t.CoverageLevel)).ToList(),
            ActivityEventCount: activityCount);
    }

    public async Task<SessionSummaryDto?> GetSessionSummaryAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return null;
        if (GetUserRole(session, userId) == null) return null;

        // Block counts grouped by the block's effective type (current
        // overrides original). Staying on ImportBlockReview while PR2 is
        // pending — PR3 renames to rev_blocks without changing this
        // aggregation shape.
        var byType = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId)
            .GroupBy(br => br.CurrentType ?? br.OriginalType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var blockCountsByType = byType.ToDictionary(x => x.Type, x => x.Count);
        var totalBlocks = byType.Sum(x => x.Count);

        // Diagnostics by severity. Two buckets matter for the summary:
        // errors (gate the Import button) and warnings (informational).
        var diagBySeverity = await _context.ImportDiagnostics
            .Where(d => d.SessionId == sessionId)
            .GroupBy(d => d.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var errorCount = diagBySeverity.FirstOrDefault(x => x.Severity == "error")?.Count ?? 0;
        var warningCount = diagBySeverity.FirstOrDefault(x => x.Severity == "warning")?.Count ?? 0;

        // Coverage % — fraction of recorded source tokens whose catalog
        // entry is full/partial/shimmed. Null when nothing recorded (non-
        // LaTeX formats currently don't populate latex_token_usage).
        var totalUsage = await _context.LatexTokenUsages
            .Where(u => u.SessionId == sessionId)
            .SumAsync(u => (int?)u.Count, ct) ?? 0;
        double? coverageMappedPercent = null;
        if (totalUsage > 0)
        {
            var coveredUsage = await _context.LatexTokenUsages
                .Where(u => u.SessionId == sessionId)
                .Join(_context.LatexTokens.Where(t => t.CoverageLevel == "full" || t.CoverageLevel == "partial" || t.CoverageLevel == "shimmed"),
                    u => u.TokenId, t => t.Id, (u, _) => u.Count)
                .SumAsync(c => (int?)c, ct) ?? 0;
            coverageMappedPercent = Math.Round(100.0 * coveredUsage / totalUsage, 1);
        }
        var unsupportedTokenCount = await _context.LatexTokenUsages
            .Where(u => u.SessionId == sessionId)
            .Join(_context.LatexTokens.Where(t => t.CoverageLevel == "unsupported" || t.CoverageLevel == "none"),
                u => u.TokenId, t => t.Id, (u, _) => u)
            .CountAsync(ct);

        // Source-text signals — only meaningful for LaTeX-ish inputs where
        // RawImportData holds the .tex / .md / etc. text. DOCX uploads
        // persist a different payload; we leave the fields null.
        int? lines = null;
        int? packageCount = null;
        string? documentClass = null;
        string? engine = null;
        if (!string.IsNullOrEmpty(session.RawImportData) && (session.SourceFormat == "latex" || session.SourceFormat == "tex" || session.SourceFormat == "markdown"))
        {
            var raw = session.RawImportData;
            lines = raw.Count(c => c == '\n') + 1;
            if (session.SourceFormat == "latex" || session.SourceFormat == "tex")
            {
                packageCount = System.Text.RegularExpressions.Regex
                    .Matches(raw, @"\\usepackage(?:\[[^\]]*\])?\{([^}]+)\}")
                    .Count;
                var classMatch = System.Text.RegularExpressions.Regex
                    .Match(raw, @"\\documentclass(?:\[[^\]]*\])?\{([^}]+)\}");
                documentClass = classMatch.Success ? classMatch.Groups[1].Value : null;
                // Naive engine sniff: fontspec / unicode-math imply xelatex/lualatex.
                engine = System.Text.RegularExpressions.Regex.IsMatch(raw, @"\\usepackage(?:\[[^\]]*\])?\{(fontspec|unicode-math)\}")
                    ? "xelatex"
                    : "pdflatex";
            }
        }

        // Estimated review heuristic (seconds → minutes). Block scan is
        // cheap (~2 s each), errors need real work (~60 s), warnings
        // require a glance (~30 s). Tune by telemetry once live.
        var estimatedSeconds = (totalBlocks * 2) + (errorCount * 60) + (warningCount * 30);
        var estimatedReviewMinutes = Math.Max(1, (int)Math.Ceiling(estimatedSeconds / 60.0));

        return new SessionSummaryDto(
            SessionId: sessionId,
            Status: session.Status,
            SourceFileName: session.DocumentTitle,
            SourceFormat: session.SourceFormat,
            Lines: lines,
            PackageCount: packageCount,
            DocumentClass: documentClass,
            Engine: engine,
            BlockCountsByType: blockCountsByType,
            TotalBlocks: totalBlocks,
            CoverageMappedPercent: coverageMappedPercent,
            UnsupportedTokenCount: unsupportedTokenCount,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            QualityScore: session.QualityScore,
            EstimatedReviewMinutes: estimatedReviewMinutes);
    }

    public async Task<List<ReviewSessionSummaryDto>> ListSessionsAsync(string userId, string scope = "active", string? format = null, DateTime? from = null, DateTime? to = null, Guid? documentId = null, CancellationToken ct = default)
    {
        var q = _context.ImportReviewSessions.Where(s => s.OwnerId == userId);

        q = scope switch
        {
            "history" => q.Where(s => s.Status == "imported" || s.Status == "cancelled"),
            "all" => q,
            _ => q.Where(s => s.Status != "imported" && s.Status != "cancelled"),
        };

        if (!string.IsNullOrWhiteSpace(format)) q = q.Where(s => s.SourceFormat == format);
        if (from.HasValue) q = q.Where(s => s.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(s => s.CreatedAt <= to.Value);
        if (documentId.HasValue) q = q.Where(s => s.DocumentId == documentId);

        var rows = await q
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new
            {
                s.Id,
                s.DocumentTitle,
                s.Status,
                s.CreatedAt,
                s.UpdatedAt,
                s.ExpiresAt,
                TotalBlocks = s.BlockReviews.Count(),
                ApprovedBlocks = s.BlockReviews.Count(br => br.Status == "approved" || br.Status == "edited"),
                RejectedBlocks = s.BlockReviews.Count(br => br.Status == "rejected"),
                PendingBlocks = s.BlockReviews.Count(br => br.Status == "pending"),
            })
            .ToListAsync(ct);

        return rows.Select(r => new ReviewSessionSummaryDto(
            r.Id, r.DocumentTitle, r.Status,
            r.TotalBlocks, r.ApprovedBlocks, r.RejectedBlocks, r.PendingBlocks,
            r.CreatedAt, r.UpdatedAt, r.ExpiresAt
        )).ToList();
    }

    public async Task<List<BlockReviewDto>> ListBlocksByAspectAsync(Guid sessionId, string userId, string aspect, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return new List<BlockReviewDto>();
        if (GetUserRole(session, userId) == null) return new List<BlockReviewDto>();

        var typeSet = aspect switch
        {
            "structure" => new[] { "heading", "tableOfContents" },
            "content"   => new[] { "paragraph", "blockquote", "list", "abstract", "code" },
            "tables"    => new[] { "table" },
            "media"     => new[] { "figure", "image" },
            "math"      => new[] { "equation", "theorem" },
            "citations" => new[] { "bibliography" },
            _           => Array.Empty<string>(),
        };

        var query = _context.ImportBlockReviews.Where(br => br.SessionId == sessionId);
        if (typeSet.Length > 0)
        {
            query = query.Where(br => typeSet.Contains(br.CurrentType ?? br.OriginalType));
        }

        var reviews = await query
            .OrderBy(br => br.SortOrder)
            .Include(br => br.Reviewer)
            .ToListAsync(ct);

        var commentCounts = new Dictionary<string, (int Total, int Unresolved)>();
        return reviews.Select(br =>
        {
            commentCounts.TryGetValue(br.BlockId, out var counts);
            return MapBlockReviewToDto(br, counts.Total, counts.Unresolved);
        }).ToList();
    }

    public async Task<BlockSourceDto?> GetBlockSourceAsync(Guid sessionId, string blockId, string userId, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return null;
        if (GetUserRole(session, userId) == null) return null;

        var review = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId && br.BlockId == blockId)
            .Select(br => new
            {
                br.BlockId,
                br.SourceRange,
                br.SourceFile,
                Type = br.CurrentType ?? br.OriginalType,
                Content = br.CurrentContent ?? br.OriginalContent,
            })
            .FirstOrDefaultAsync(ct);
        if (review == null) return null;
        var contentJson = JsonSerializer.Deserialize<JsonElement>(review.Content.RootElement.GetRawText());

        // Preferred path — parser populated source_range during staging.
        // Slice the session's RawImportData at (start..end) and return.
        if (review.SourceRange != null
            && review.SourceRange.RootElement.TryGetProperty("start", out var startEl)
            && review.SourceRange.RootElement.TryGetProperty("end", out var endEl)
            && startEl.TryGetInt32(out var start)
            && endEl.TryGetInt32(out var end)
            && !string.IsNullOrEmpty(session.RawImportData)
            && start >= 0 && end > start && end <= session.RawImportData.Length)
        {
            var slice = session.RawImportData[start..end];
            return new BlockSourceDto(blockId, slice, "parser", start, end, review.SourceFile, review.Type, contentJson);
        }

        // Fallback — legacy sessions don't have source_range populated.
        // Re-render the block's current JSONB content to LaTeX via
        // RenderService so the Source sub-tab still shows meaningful
        // LaTeX. Marked origin="render" so the UI can flag it as a
        // reconstructed view rather than the literal import source.
        if (_renderService != null)
        {
            try
            {
                var block = new Block
                {
                    Id = Guid.NewGuid(),
                    DocumentId = sessionId,
                    Type = review.Type,
                    Content = review.Content,
                };
                var latex = _renderService.RenderBlockToLatex(block);
                return new BlockSourceDto(blockId, latex, "render", null, null, review.SourceFile, review.Type, contentJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImportReview] RenderBlockToLatex failed for block {BlockId} — returning empty source", blockId);
            }
        }

        return new BlockSourceDto(blockId, string.Empty, "none", null, null, review.SourceFile, review.Type, contentJson);
    }

    public async Task<bool> SetTabProgressAsync(Guid sessionId, string userId, SetTabProgressDto dto, CancellationToken ct = default)
    {
        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return false;
        if (GetUserRole(session, userId) == null) return false;

        // Accept any tab name — vocabulary is UI-enforced so adding a new
        // tab doesn't need a schema change. State is pinned to the three
        // agreed values.
        if (dto.State is not ("unvisited" or "in_progress" or "done")) return false;

        // Merge into existing tab_progress jsonb via jsonb_set in SQL so
        // we don't round-trip the whole blob. DB-first per the project
        // guideline. jsonb_build_object() is used in place of the literal
        // '{}'::jsonb because EF's ExecuteSqlRawAsync scans the SQL for
        // {N} placeholders and throws FormatException on the literal {}
        // (LILIA-API-S, 2026-04-21).
        await _context.Database.ExecuteSqlRawAsync(@"
UPDATE import_review_sessions
SET tab_progress = jsonb_set(
      COALESCE(tab_progress, jsonb_build_object()),
      ARRAY[@tab]::text[],
      to_jsonb(@state::text),
      true),
    last_focused_tab = @tab,
    updated_at = NOW()
WHERE id = @id;",
            new Npgsql.NpgsqlParameter("tab", dto.Tab),
            new Npgsql.NpgsqlParameter("state", dto.State),
            new Npgsql.NpgsqlParameter("id", sessionId));

        return true;
    }

    public async Task<BatchConvertResultDto?> BatchConvertBlockReviewsAsync(Guid sessionId, string userId, BatchConvertReviewBlocksDto dto)
    {
        if (dto.BlockIds.Count == 0) return null;

        var session = await _context.ImportReviewSessions
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return null;

        var role = GetUserRole(session, userId);
        if (role == null || role == "viewer") return null;

        var metas = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId && dto.BlockIds.Contains(br.BlockId))
            .OrderBy(br => br.SortOrder)
            .Select(br => new { br.BlockId, EffectiveType = br.CurrentType ?? br.OriginalType })
            .ToListAsync();

        if (metas.Count != dto.BlockIds.Count) return null;

        switch (dto.Action)
        {
            case "to_list":
                return await FoldReviewsSqlAsync(session, metas.Select(m => (m.BlockId, m.EffectiveType)).ToList(), userId, ordered: false);
            case "to_ordered_list":
                return await FoldReviewsSqlAsync(session, metas.Select(m => (m.BlockId, m.EffectiveType)).ToList(), userId, ordered: true);
            case "merge_paragraph":
                return await MergeReviewsSqlAsync(session, metas.Select(m => m.BlockId).ToList(), userId);
            case "reheading":
                if (dto.HeadingLevel is null or < 1 or > 6) return null;
                return await ReheadingReviewsSqlAsync(session, metas.Where(m => m.EffectiveType == "heading").Select(m => m.BlockId).ToList(), userId, dto.HeadingLevel.Value);
            default:
                return null;
        }
    }

    private async Task<BatchConvertResultDto> FoldReviewsSqlAsync(ImportReviewSession session, List<(string BlockId, string Type)> metas, string userId, bool ordered)
    {
        var treatFirstAsLabel = metas.Count >= 2 && metas[0].Type == "heading" && metas.Skip(1).All(m => m.Type != "heading");
        var foldIds = (treatFirstAsLabel ? metas.Skip(1) : metas).Select(m => m.BlockId).ToArray();
        var hostId = foldIds[0];
        var deleteIds = foldIds.Skip(1).ToArray();

        // When a folded row is already a list, we expand its items[] array
        // so the merged result inherits every child item rather than dropping
        // to an empty list (previous bug — COALESCE only looked at scalar
        // text fields). `fold` builds a jsonb array per row — single-item
        // array for scalar blocks, passthrough for existing lists — and
        // `flat` flattens via jsonb_array_elements_text.
        const string sql = @"
WITH fold AS (
  SELECT block_id,
         sort_order,
         CASE
           WHEN jsonb_typeof(COALESCE(current_content, original_content)->'items') = 'array'
             THEN COALESCE(current_content, original_content)->'items'
           ELSE jsonb_build_array(
             COALESCE(
               NULLIF(COALESCE(current_content, original_content)->>'text', ''),
               NULLIF(COALESCE(current_content, original_content)->>'title', ''),
               NULLIF(COALESCE(current_content, original_content)->>'caption', ''),
               NULLIF(COALESCE(current_content, original_content)->>'code', ''),
               NULLIF(COALESCE(current_content, original_content)->>'latex', ''),
               NULLIF(COALESCE(current_content, original_content)->>'name', ''),
               ''
             )
           )
         END AS items_arr
  FROM import_block_reviews
  WHERE session_id = @session AND block_id = ANY(@fold_ids)
),
flat AS (
  SELECT item_text, f.sort_order, t.ord
  FROM fold f,
       LATERAL jsonb_array_elements_text(f.items_arr) WITH ORDINALITY AS t(item_text, ord)
  WHERE item_text <> ''
)
UPDATE import_block_reviews br
SET current_type = 'list',
    current_content = jsonb_build_object(
      'items',   COALESCE((SELECT jsonb_agg(to_jsonb(item_text) ORDER BY sort_order, ord) FROM flat), '[]'::jsonb),
      'ordered', @ordered::boolean),
    status = 'edited',
    reviewed_by = @user,
    reviewed_at = NOW()
WHERE br.session_id = @session AND br.block_id = @host;";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("session", session.Id),
            new Npgsql.NpgsqlParameter("fold_ids", foldIds),
            new Npgsql.NpgsqlParameter("ordered", ordered),
            new Npgsql.NpgsqlParameter("user", userId),
            new Npgsql.NpgsqlParameter("host", hostId));

        if (deleteIds.Length > 0)
        {
            await _context.ImportBlockReviews
                .Where(br => br.SessionId == session.Id && deleteIds.Contains(br.BlockId))
                .ExecuteDeleteAsync();
        }

        await LogActivityAsync(session.Id, userId, "blocks_folded", hostId, new { targetType = "list", ordered, count = metas.Count });

        return await ProjectReviewsAsync(session.Id, treatFirstAsLabel ? new[] { metas[0].BlockId, hostId } : new[] { hostId });
    }

    private async Task<BatchConvertResultDto> MergeReviewsSqlAsync(ImportReviewSession session, List<string> ids, string userId)
    {
        var hostId = ids[0];
        var deleteIds = ids.Skip(1).ToArray();

        // Merge handles list-shaped sources too — their items get joined with
        // newlines into the merged paragraph text.
        const string sql = @"
WITH parts AS (
  SELECT sort_order,
         CASE
           WHEN jsonb_typeof(COALESCE(current_content, original_content)->'items') = 'array'
             THEN (
               SELECT string_agg(item_text, E'\n')
               FROM jsonb_array_elements_text(COALESCE(current_content, original_content)->'items') AS item_text
               WHERE item_text <> ''
             )
           ELSE COALESCE(
             NULLIF(COALESCE(current_content, original_content)->>'text', ''),
             NULLIF(COALESCE(current_content, original_content)->>'title', ''),
             NULLIF(COALESCE(current_content, original_content)->>'caption', ''),
             NULLIF(COALESCE(current_content, original_content)->>'code', ''),
             NULLIF(COALESCE(current_content, original_content)->>'latex', ''),
             NULLIF(COALESCE(current_content, original_content)->>'name', ''),
             ''
           )
         END AS text
  FROM import_block_reviews
  WHERE session_id = @session AND block_id = ANY(@ids)
)
UPDATE import_block_reviews br
SET current_type = 'paragraph',
    current_content = jsonb_build_object(
      'text', COALESCE((SELECT string_agg(text, E'\n\n' ORDER BY sort_order) FROM parts WHERE text <> ''), '')),
    status = 'edited',
    reviewed_by = @user,
    reviewed_at = NOW()
WHERE br.session_id = @session AND br.block_id = @host;";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("session", session.Id),
            new Npgsql.NpgsqlParameter("ids", ids.ToArray()),
            new Npgsql.NpgsqlParameter("user", userId),
            new Npgsql.NpgsqlParameter("host", hostId));

        if (deleteIds.Length > 0)
        {
            await _context.ImportBlockReviews
                .Where(br => br.SessionId == session.Id && deleteIds.Contains(br.BlockId))
                .ExecuteDeleteAsync();
        }

        await LogActivityAsync(session.Id, userId, "blocks_merged", hostId, new { count = ids.Count });
        return await ProjectReviewsAsync(session.Id, new[] { hostId });
    }

    private async Task<BatchConvertResultDto> ReheadingReviewsSqlAsync(ImportReviewSession session, List<string> headingIds, string userId, int level)
    {
        if (headingIds.Count == 0)
            return new BatchConvertResultDto(Created: new List<BlockDto>(), DeletedIds: new List<Guid>());

        const string sql = @"
UPDATE import_block_reviews br
SET current_type = 'heading',
    current_content = jsonb_build_object(
      'text', COALESCE(COALESCE(br.current_content, br.original_content)->>'text', ''),
      'level', @level::int),
    status = 'edited',
    reviewed_by = @user,
    reviewed_at = NOW()
WHERE br.session_id = @session AND br.block_id = ANY(@ids)
  AND COALESCE(br.current_type, br.original_type) = 'heading';";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("session", session.Id),
            new Npgsql.NpgsqlParameter("ids", headingIds.ToArray()),
            new Npgsql.NpgsqlParameter("level", level),
            new Npgsql.NpgsqlParameter("user", userId));

        return await ProjectReviewsAsync(session.Id, headingIds.ToArray());
    }

    private async Task LogActivityAsync(Guid sessionId, string userId, string action, string blockId, object details)
    {
        // One-row insert via EF here is fine — activity feed is write-rare
        // and the row is small. Session UpdatedAt moves via ExecuteUpdate.
        _context.ImportReviewActivities.Add(new ImportReviewActivity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Action = action,
            BlockId = blockId,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(details)),
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
        await _context.ImportReviewSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    private async Task<BatchConvertResultDto> ProjectReviewsAsync(Guid sessionId, string[] blockIds)
    {
        // Small projection just to echo the new shapes back to the client.
        var rows = await _context.ImportBlockReviews
            .Where(br => br.SessionId == sessionId && blockIds.Contains(br.BlockId))
            .OrderBy(br => br.SortOrder)
            .Select(br => new
            {
                br.BlockId,
                br.SortOrder,
                Type = br.CurrentType ?? br.OriginalType,
                Content = br.CurrentContent != null ? br.CurrentContent : br.OriginalContent,
            })
            .ToListAsync();

        var created = rows.Select(r => new BlockDto(
            Guid.Empty, sessionId, r.Type, r.Content.RootElement, r.SortOrder, null, 0, DateTime.UtcNow, DateTime.UtcNow))
            .ToList();
        return new BatchConvertResultDto(Created: created, DeletedIds: new List<Guid>());
    }

    // ─────────────────────────────────────────────────────────────────
    //  Overleaf zip asset staging — called at finalize time.
    //  Re-extracts the preserved zip, uploads files to R2 + creates
    //  Asset rows, then wires images / .bib / code listings into the
    //  corresponding blocks on the finalized document.
    // ─────────────────────────────────────────────────────────────────

    private async Task StageZipAssetsAsync(
        Guid sessionId, Guid documentId, string ownerId, DateTime now, CancellationToken ct)
    {
        if (_storageService is null || _projectExtractor is null) return;

        // Jobs.Id is the key for the saved zip. Look it up via the session.
        var jobId = await _context.ImportReviewSessions
            .Where(s => s.Id == sessionId)
            .Select(s => s.JobId)
            .FirstOrDefaultAsync(ct);
        if (jobId == null || jobId == Guid.Empty) return;

        var zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "uploads", "imports", $"{jobId}.zip");
        if (!File.Exists(zipPath)) return;

        byte[] zipBytes;
        try
        {
            zipBytes = await File.ReadAllBytesAsync(zipPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ImportReview] Could not read zip at {Path}", zipPath);
            return;
        }

        Lilia.Import.Services.LatexProjectResult extracted;
        try
        {
            extracted = _projectExtractor.Extract(zipBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ImportReview] Re-extract failed for session {SessionId}", sessionId);
            return;
        }

        // Per-filename → Asset url map, used to rewrite figure blocks'
        // src attribute and code-listing block content after upload.
        var byFilename = new Dictionary<string, (Guid AssetId, string Url)>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in extracted.Files)
        {
            var assetId = Guid.NewGuid();
            var ext = Path.GetExtension(f.Path);
            var storageKey = $"{ownerId}/documents/{documentId}/import-assets/{assetId}{ext}";

            // Upload to storage. Failures are logged + skipped — we don't
            // want one bad asset to kill the finalize transaction.
            string url;
            try
            {
                using var stream = new MemoryStream(f.Bytes);
                await _storageService.UploadAsync(storageKey, stream, f.ContentType);
                url = _storageService.GetPublicUrl(storageKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImportReview] Failed to upload asset {Path}", f.Path);
                continue;
            }

            _context.Assets.Add(new Asset
            {
                Id = assetId,
                DocumentId = documentId,
                FileName = Path.GetFileName(f.Path),
                FileType = f.ContentType,
                FileSize = f.Bytes.Length,
                StorageKey = storageKey,
                Url = url,
                UserId = ownerId,
                CreatedAt = now,
            });

            // Map by both full path and bare filename so \includegraphics{foo}
            // and \includegraphics{./img/foo.png} both resolve.
            byFilename[f.Path] = (assetId, url);
            byFilename[Path.GetFileName(f.Path)] = (assetId, url);
            byFilename[Path.GetFileNameWithoutExtension(f.Path)] = (assetId, url);
        }
        await _context.SaveChangesAsync(ct);

        // Wire 1/3 — rewrite figure blocks' content.src to the R2 URL
        // when src matches a staged image filename (with/without extension).
        var images = extracted.Files
            .Where(f => f.Kind == Lilia.Import.Services.LatexProjectFileKinds.Image)
            .ToList();
        if (images.Count > 0)
        {
            await RewriteFigureBlocksToAssetsAsync(documentId, byFilename, ct);
        }

        // Wire 2/3 — parse .bib files and create BibliographyEntry rows.
        var bibs = extracted.Files
            .Where(f => f.Kind == Lilia.Import.Services.LatexProjectFileKinds.Bib)
            .ToList();
        foreach (var bib in bibs)
        {
            try
            {
                var content = System.Text.Encoding.UTF8.GetString(bib.Bytes);
                var entries = BibTexParser.Parse(content);
                foreach (var e in entries)
                {
                    _context.BibliographyEntries.Add(new BibliographyEntry
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = documentId,
                        CiteKey = e.CiteKey,
                        EntryType = e.EntryType,
                        Data = JsonDocument.Parse(JsonSerializer.Serialize(e.Fields)),
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
                _logger.LogInformation("[ImportReview] Imported {Count} bibliography entries from {Path}", entries.Count, bib.Path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImportReview] Failed to parse .bib {Path}", bib.Path);
            }
        }
        await _context.SaveChangesAsync(ct);

        // Wire 3/3 — \lstinputlisting{foo.py} wire-up is deferred
        // post-launch. The referenced files are already staged as
        // Asset rows, just not inlined into code blocks yet. Users
        // can view them via the asset URL.

        // Surface extractor notices as ImportDiagnostic rows so the
        // review panel shows the summary. Info severity because these
        // are informational, not blocking.
        foreach (var notice in extracted.Notices)
        {
            _context.ImportDiagnostics.Add(new ImportDiagnostic
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Severity = "warning",
                Category = "auto_shimmed",
                Message = notice,
                CreatedAt = now,
            });
        }
        if (extracted.Notices.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    private async Task RewriteFigureBlocksToAssetsAsync(
        Guid documentId,
        Dictionary<string, (Guid AssetId, string Url)> byFilename,
        CancellationToken ct)
    {
        var figureBlocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId && b.Type == "figure")
            .ToListAsync(ct);

        var updated = 0;
        foreach (var block in figureBlocks)
        {
            var contentObj = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                block.Content.RootElement.GetRawText()) ?? new();
            if (!contentObj.TryGetValue("src", out var srcObj)) continue;
            var src = srcObj?.ToString();
            if (string.IsNullOrEmpty(src)) continue;

            // Try full path, then bare filename, then without extension.
            if (!byFilename.TryGetValue(src, out var hit))
            {
                var fname = Path.GetFileName(src);
                if (!byFilename.TryGetValue(fname, out hit))
                {
                    var bare = Path.GetFileNameWithoutExtension(src);
                    if (!byFilename.TryGetValue(bare, out hit)) continue;
                }
            }

            contentObj["src"] = hit.Url;
            contentObj["assetId"] = hit.AssetId.ToString();
            block.Content = JsonDocument.Parse(JsonSerializer.Serialize(contentObj));
            block.UpdatedAt = DateTime.UtcNow;
            updated++;
        }
        if (updated > 0) await _context.SaveChangesAsync(ct);
        _logger.LogInformation("[ImportReview] Rewrote {Count} figure blocks to R2 asset URLs", updated);
    }
}
