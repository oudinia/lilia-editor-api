using System.Text.Json;

namespace Lilia.Core.DTOs;

public record CreateFeedbackDto(
    string Type,
    string Message,
    string? Page,
    string? BlockType,
    string? BlockId,
    string? DocumentId,
    JsonElement? Metadata
);

public record FeedbackDto(
    Guid Id,
    string? UserId,
    string? UserName,
    string Type,
    string Message,
    string? Page,
    string? BlockType,
    string? DocumentId,
    string Status,
    string? Response,
    JsonElement? Metadata,
    DateTime CreatedAt
);

public record UpdateFeedbackDto(
    string? Status,
    string? Response
);
