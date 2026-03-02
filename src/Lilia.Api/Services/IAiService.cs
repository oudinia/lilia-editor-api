using System.Text.Json;
using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IAiService
{
    // AI features
    Task<GenerateBlockResponse> GenerateBlockAsync(string prompt, GenerateBlockContext? context = null);
    Task<ImproveTextResponse> ImproveTextAsync(string text, string action);
    Task<SuggestEquationResponse> SuggestEquationAsync(string description);
    Task<GrammarCheckResponse> GrammarCheckAsync(string text);
    Task<CitationCheckResponse> CitationCheckAsync(string text);
    Task<GenerateAbstractResponse> GenerateAbstractAsync(string title, string content);

    // Chat CRUD
    Task<AiChatDto> CreateChatAsync(string userId, CreateAiChatRequest request);
    Task<AiChatListResponse> GetChatsAsync(string userId, string? organizationId = null);
    Task<AiChatDto?> GetChatAsync(string userId, string id);
    Task<AiChatDto?> UpdateChatAsync(string userId, string id, UpdateAiChatRequest request);
    Task<bool> DeleteChatAsync(string userId, string id);
}
