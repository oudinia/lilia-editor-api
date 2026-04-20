using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IImportReviewService
{
    // Session management
    Task<CreateSessionResponseDto> CreateSessionAsync(string userId, CreateReviewSessionDto dto);
    Task<SessionDataDto?> GetSessionAsync(Guid sessionId, string userId);
    Task<bool> CancelSessionAsync(Guid sessionId, string userId, bool permanent = false);
    Task<FinalizeResultDto?> FinalizeSessionAsync(Guid sessionId, string userId, FinalizeSessionDto dto);

    // Block operations
    Task<BlockReviewDto?> UpdateBlockReviewAsync(Guid sessionId, string blockId, string userId, UpdateBlockReviewDto dto);
    Task<BlockReviewDto?> ResetBlockAsync(Guid sessionId, string blockId, string userId);
    Task<int> BulkActionAsync(Guid sessionId, string userId, BulkActionDto dto);

    /// <summary>
    /// Tier 1 bulk-convert against staged blocks. Same four actions as the
    /// document-level endpoint (to_list, to_ordered_list, merge_paragraph,
    /// reheading) but operates on ImportBlockReview rows pre-finalize —
    /// fixes the CV "3 headings that should be a list" case before the
    /// import promotes into real Blocks.
    /// </summary>
    Task<BatchConvertResultDto?> BatchConvertBlockReviewsAsync(Guid sessionId, string userId, BatchConvertReviewBlocksDto dto);

    // Collaborators
    Task<CollaboratorInfoDto?> AddCollaboratorAsync(Guid sessionId, string userId, AddReviewCollaboratorDto dto);
    Task<bool> RemoveCollaboratorAsync(Guid sessionId, string targetUserId, string userId);

    // Comments
    Task<ReviewCommentDto?> AddCommentAsync(Guid sessionId, string userId, AddReviewCommentDto dto);
    Task<bool> UpdateCommentAsync(Guid sessionId, Guid commentId, string userId, UpdateReviewCommentDto dto);
    Task<bool> DeleteCommentAsync(Guid sessionId, Guid commentId, string userId);
    Task<List<ReviewCommentDto>> GetCommentsAsync(Guid sessionId, string userId, string? blockId = null);

    // Activity
    Task<List<ReviewActivityDto>> GetActivitiesAsync(Guid sessionId, string userId, int limit = 50);
    Task<List<ReviewActivityDto>> GetRecentActivitiesAsync(Guid sessionId, string userId, DateTime since);

    // Called by JobService to auto-create session from DOCX import
    Task<CreateSessionResponseDto> CreateSessionFromImportAsync(
        string userId,
        Guid jobId,
        string documentTitle,
        List<CreateReviewBlockDto> blocks,
        List<object>? warnings = null,
        System.Text.Json.JsonElement? paragraphTraces = null,
        string? sourceFilePath = null,
        string? rawImportData = null
    );

    // Paragraph traces
    Task<System.Text.Json.JsonElement?> GetParagraphTracesAsync(Guid sessionId, string userId);

    // Called by LatexImportJobExecutor when auto-finalize gate passes. Skips
    // permission + force-flag logic because the executor already proved the
    // session is clean (0 errors, 0 risky warnings).
    Task<FinalizeResultDto> FinalizeFromStagingAsync(
        Guid sessionId,
        string ownerId,
        string documentTitle,
        bool force,
        CancellationToken ct = default
    );

    // Diagnostics (new import-review feature)
    Task<List<ImportDiagnosticDto>> GetDiagnosticsAsync(Guid sessionId, string userId);
    Task<ImportDiagnosticDto?> DismissDiagnosticAsync(Guid sessionId, Guid diagnosticId, string userId);

    // Document category — unlocks category-specialised finding rules.
    Task<bool> SetSessionCategoryAsync(Guid sessionId, string userId, string? category);
}
