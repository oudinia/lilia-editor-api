using System.Text.Json;
using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IAiService
{
    // AI features
    Task<GenerateBlockResponse> GenerateBlockAsync(string prompt, GenerateBlockContext? context = null);
    /// <summary>
    /// Revise an existing block. <paramref name="compilerError"/> is fed back on
    /// a retry: the model gets to see why LaTeX rejected its last attempt,
    /// which is the difference between one guess and a corrected one.
    /// </summary>
    Task<ReviseBlockResult> ReviseBlockAsync(
        string type, JsonElement content, string instruction, string? compilerError = null);
    Task<ImproveTextResponse> ImproveTextAsync(string text, string action);
    Task<SuggestEquationResponse> SuggestEquationAsync(string description);
    Task<GrammarCheckResponse> GrammarCheckAsync(string text);
    Task<CitationCheckResponse> CitationCheckAsync(string text);
    Task<GenerateAbstractResponse> GenerateAbstractAsync(string title, string content);
    /// <summary>One-sentence gist of a document (for the My Documents cards).</summary>
    Task<string> GenerateOneLinerAsync(string title, string content);

    // Chat CRUD
    Task<AiChatDto> CreateChatAsync(string userId, CreateAiChatRequest request);
    Task<AiChatListResponse> GetChatsAsync(string userId, string? organizationId = null);
    Task<AiChatDto?> GetChatAsync(string userId, string id);
    Task<AiChatDto?> UpdateChatAsync(string userId, string id, UpdateAiChatRequest request);
    Task<bool> DeleteChatAsync(string userId, string id);
}
