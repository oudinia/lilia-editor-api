using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class AiConversationService : IAiConversationService
{
    private readonly LiliaDbContext _context;
    private readonly IDocumentService _documentService;

    public AiConversationService(LiliaDbContext context, IDocumentService documentService)
    {
        _context = context;
        _documentService = documentService;
    }

    public async Task<List<AiConversationListDto>> ListAsync(string userId, Guid? documentId)
    {
        var query = _context.AiConversations
            .Where(c => c.OwnerId == userId && c.ArchivedAt == null);
        if (documentId.HasValue)
            query = query.Where(c => c.DocumentId == documentId.Value);

        return await query
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new AiConversationListDto(
                c.Id, c.DocumentId, c.Title, c.Messages.Count, c.CreatedAt, c.UpdatedAt))
            .ToListAsync();
    }

    public async Task<AiConversationDto?> GetAsync(string userId, Guid conversationId)
    {
        var conv = await _context.AiConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv is null || conv.OwnerId != userId) return null;
        return ToDto(conv);
    }

    public async Task<AiConversationDto> CreateAsync(string userId, CreateConversationDto dto)
    {
        // If scoped to a document, require write access to it.
        if (dto.DocumentId.HasValue)
            await EnsureDocWriteAsync(dto.DocumentId.Value, userId);

        var conv = new AiConversation
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            DocumentId = dto.DocumentId,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? "New chat" : dto.Title!.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.AiConversations.Add(conv);
        await _context.SaveChangesAsync();
        return ToDto(conv);
    }

    public async Task<AiMessageDto?> AppendMessageAsync(string userId, Guid conversationId, AppendMessageDto dto)
    {
        var conv = await _context.AiConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv is null || conv.OwnerId != userId) return null;

        var nextSort = conv.Messages.Count == 0 ? 0 : conv.Messages.Max(m => m.SortOrder) + 1;
        var msg = new AiMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            Role = dto.Role,
            Content = JsonDocument.Parse(dto.Content.GetRawText()),
            CreditsUsed = dto.CreditsUsed,
            SortOrder = nextSort,
            CreatedAt = DateTime.UtcNow,
        };
        _context.AiMessages.Add(msg);

        // First user message titles the conversation.
        if (conv.Title == "New chat" && dto.Role == "user")
        {
            var text = TryReadText(dto.Content);
            if (!string.IsNullOrWhiteSpace(text))
                conv.Title = text!.Length > 60 ? text[..60] : text;
        }
        conv.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new AiMessageDto(msg.Id, msg.Role, msg.Content.RootElement.Clone(), msg.CreditsUsed, msg.SortOrder, msg.CreatedAt);
    }

    public async Task<bool> RenameAsync(string userId, Guid conversationId, string title)
    {
        var conv = await _context.AiConversations.FindAsync(conversationId);
        if (conv is null || conv.OwnerId != userId) return false;
        conv.Title = string.IsNullOrWhiteSpace(title) ? conv.Title : title.Trim();
        conv.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MoveAsync(string userId, Guid conversationId, Guid? documentId)
    {
        var conv = await _context.AiConversations.FindAsync(conversationId);
        if (conv is null || conv.OwnerId != userId) return false;
        if (documentId.HasValue)
            await EnsureDocWriteAsync(documentId.Value, userId);
        conv.DocumentId = documentId;
        conv.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<AiConversationDto?> CloneAsync(string userId, Guid conversationId, CloneConversationDto dto)
    {
        var source = await _context.AiConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (source is null || source.OwnerId != userId) return null;
        if (dto.DocumentId.HasValue)
            await EnsureDocWriteAsync(dto.DocumentId.Value, userId);

        var clone = new AiConversation
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            DocumentId = dto.DocumentId,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? source.Title : dto.Title!.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Messages = source.Messages
                .OrderBy(m => m.SortOrder)
                .Select(m => new AiMessage
                {
                    Id = Guid.NewGuid(),
                    Role = m.Role,
                    Content = JsonDocument.Parse(m.Content.RootElement.GetRawText()),
                    CreditsUsed = m.CreditsUsed,
                    SortOrder = m.SortOrder,
                    CreatedAt = DateTime.UtcNow,
                })
                .ToList(),
        };
        _context.AiConversations.Add(clone);
        await _context.SaveChangesAsync();
        return ToDto(clone);
    }

    public async Task<bool> DeleteAsync(string userId, Guid conversationId)
    {
        var conv = await _context.AiConversations.FindAsync(conversationId);
        if (conv is null || conv.OwnerId != userId) return false;
        _context.AiConversations.Remove(conv); // cascade removes messages
        await _context.SaveChangesAsync();
        return true;
    }

    // ── helpers ──
    private async Task EnsureDocWriteAsync(Guid documentId, string userId)
    {
        if (!await _documentService.HasAccessAsync(documentId, userId, Permissions.Write))
            throw new UnauthorizedAccessException("No write access to the target document.");
    }

    private static AiConversationDto ToDto(AiConversation c) => new(
        c.Id, c.DocumentId, c.Title, c.CreatedAt, c.UpdatedAt,
        c.Messages.OrderBy(m => m.SortOrder).Select(m =>
            new AiMessageDto(m.Id, m.Role, m.Content.RootElement.Clone(), m.CreditsUsed, m.SortOrder, m.CreatedAt)).ToList());

    private static string? TryReadText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            return t.GetString();
        return null;
    }
}
