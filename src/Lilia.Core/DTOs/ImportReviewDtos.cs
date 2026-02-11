using System.Text.Json;

namespace Lilia.Core.DTOs;

// --- Request DTOs ---

public record CreateReviewSessionDto(
    Guid? JobId,
    string DocumentTitle,
    List<CreateReviewBlockDto> Blocks,
    JsonElement? Warnings = null
);

public record CreateReviewBlockDto(
    string Id,
    string Type,
    JsonElement Content,
    int Confidence,
    JsonElement? Warnings,
    int SortOrder,
    int Depth
);

public record UpdateBlockReviewDto(
    string SessionId,
    string BlockId,
    string? Status = null,
    JsonElement? CurrentContent = null,
    string? CurrentType = null
);

public record BulkActionDto(
    string SessionId,
    string Action, // approveAll, rejectErrors, approveHighConfidence, resetAll, approveSelected, rejectSelected
    List<string>? BlockIds = null
);

public record FinalizeSessionDto(
    string? DocumentTitle = null,
    bool Force = false
);

public record AddReviewCollaboratorDto(
    string SessionId,
    string Email,
    string Role = "reviewer" // reviewer, viewer
);

public record AddReviewCommentDto(
    string SessionId,
    string BlockId,
    string Content
);

public record UpdateReviewCommentDto(
    string SessionId,
    string CommentId,
    bool Resolved
);

// --- Response DTOs ---

public record ReviewSessionInfoDto(
    Guid Id,
    Guid? JobId,
    string OwnerId,
    string DocumentTitle,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ExpiresAt,
    Guid? DocumentId,
    JsonElement? OriginalWarnings
);

public record BlockReviewDto(
    Guid Id,
    string BlockId,
    int BlockIndex,
    string Status,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    ReviewUserDto? Reviewer,
    JsonElement OriginalContent,
    string OriginalType,
    JsonElement? CurrentContent,
    string? CurrentType,
    int? Confidence,
    JsonElement? Warnings,
    int SortOrder,
    int Depth,
    int CommentCount,
    int UnresolvedCommentCount
);

public record ReviewUserDto(
    string Id,
    string? Name,
    string? Image
);

public record ReviewUserWithEmailDto(
    string Id,
    string? Name,
    string? Email,
    string? Image
);

public record SessionDataDto(
    ReviewSessionInfoDto Session,
    ReviewUserDto Owner,
    List<BlockReviewDto> Blocks,
    List<CollaboratorInfoDto> Collaborators,
    string UserRole
);

public record CollaboratorInfoDto(
    string UserId,
    string Role,
    DateTime InvitedAt,
    DateTime? LastActiveAt,
    ReviewUserWithEmailDto User
);

public record FinalizeResultDto(
    FinalizedDocumentDto Document,
    FinalizeStatisticsDto Statistics
);

public record FinalizedDocumentDto(
    Guid Id,
    string Title
);

public record FinalizeStatisticsDto(
    int ImportedBlocks,
    int SkippedBlocks
);

public record ReviewCommentDto(
    Guid Id,
    string BlockId,
    string Content,
    bool Resolved,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ReviewUserDto User,
    ReviewUserDto? Resolver
);

public record ReviewActivityDto(
    Guid Id,
    string Action,
    string? BlockId,
    JsonElement? Details,
    DateTime CreatedAt,
    ReviewUserDto User
);

// --- Wrapper response DTOs ---

public record CreateSessionResponseDto(
    ReviewSessionInfoDto Session,
    List<BlockReviewDto> Blocks
);

public record CollaboratorResponseDto(
    CollaboratorInfoDto Collaborator
);

public record CommentResponseDto(
    ReviewCommentDto Comment
);

public record CommentsListDto(
    List<ReviewCommentDto> Comments
);

public record ActivitiesListDto(
    List<ReviewActivityDto> Activities
);
