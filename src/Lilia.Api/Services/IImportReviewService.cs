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
        List<object>? warnings = null
    );
}
