using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

/// <summary>
/// Durable, server-owned Ask Lilia conversations. Scoped by document, owner-gated,
/// and reassignable (move) or cloneable (copy) across documents.
/// </summary>
public interface IAiConversationService
{
    Task<List<AiConversationListDto>> ListAsync(string userId, Guid? documentId);
    Task<AiConversationDto?> GetAsync(string userId, Guid conversationId);
    Task<AiConversationDto> CreateAsync(string userId, CreateConversationDto dto);
    Task<AiMessageDto?> AppendMessageAsync(string userId, Guid conversationId, AppendMessageDto dto);
    Task<bool> RenameAsync(string userId, Guid conversationId, string title);
    Task<bool> MoveAsync(string userId, Guid conversationId, Guid? documentId);
    Task<AiConversationDto?> CloneAsync(string userId, Guid conversationId, CloneConversationDto dto);
    Task<bool> DeleteAsync(string userId, Guid conversationId);
}
