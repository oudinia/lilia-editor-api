namespace Lilia.Core.DTOs;

public record CommentDto(
    Guid Id,
    Guid DocumentId,
    Guid? BlockId,
    string UserId,
    string? UserName,
    string Content,
    bool Resolved,
    List<CommentReplyDto> Replies,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CommentReplyDto(
    Guid Id,
    string UserId,
    string? UserName,
    string Content,
    DateTime CreatedAt
);

public record CreateCommentDto(Guid? BlockId, string Content);

public record CreateReplyDto(string Content);

public record UpdateCommentDto(string? Content, bool? Resolved);
