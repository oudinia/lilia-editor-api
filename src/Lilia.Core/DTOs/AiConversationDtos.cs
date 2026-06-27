using System.Text.Json;

namespace Lilia.Core.DTOs;

/// <summary>Lightweight row for the conversation list / switcher.</summary>
public record AiConversationListDto(
    Guid Id,
    Guid? DocumentId,
    string Title,
    int MessageCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Full conversation with its messages, for restoring a thread.</summary>
public record AiConversationDto(
    Guid Id,
    Guid? DocumentId,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AiMessageDto> Messages
);

public record AiMessageDto(
    Guid Id,
    string Role,
    JsonElement Content,
    int CreditsUsed,
    int SortOrder,
    DateTime CreatedAt
);

// ── Requests ──
public record CreateConversationDto(Guid? DocumentId, string? Title);

/// <summary>Append a single turn. Content is the verbatim client turn payload.</summary>
public record AppendMessageDto(string Role, JsonElement Content, int CreditsUsed);

public record RenameConversationDto(string Title);

/// <summary>Move (reassign) a conversation to another document (or null = general).</summary>
public record MoveConversationDto(Guid? DocumentId);

/// <summary>Clone this conversation under another document, keeping the original.</summary>
public record CloneConversationDto(Guid? DocumentId, string? Title);
