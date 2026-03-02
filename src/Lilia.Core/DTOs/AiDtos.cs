using System.Text.Json;

namespace Lilia.Core.DTOs;

// --- Generate Block ---
public record GenerateBlockContext(string? DocumentTitle = null, string? SurroundingText = null);
public record GenerateBlockRequest(string Prompt, GenerateBlockContext? Context = null);
public record GenerateBlockResponse(string Type, JsonElement Content);

// --- Improve Text ---
public record ImproveTextRequest(string Text, string Action);
public record ImproveTextResponse(string Text);

// --- Suggest Equation ---
public record SuggestEquationRequest(string Description);
public record SuggestEquationResponse(string Latex, JsonElement? Block = null);

// --- Grammar Check ---
public record GrammarCheckRequest(string Text);
public record GrammarSuggestion(string Original, string Suggestion, string Type, string Explanation);
public record GrammarCheckResponse(List<GrammarSuggestion> Suggestions);

// --- Citation Check ---
public record CitationCheckRequest(string Text);
public record CitationSuggestion(string Sentence, string Reason, List<string> SuggestedSearchTerms);
public record CitationCheckResponse(List<CitationSuggestion> Suggestions);

// --- Generate Abstract ---
public record GenerateAbstractRequest(string Title, string Content);
public record GenerateAbstractResponse(string Text);

// --- AI Chat ---
public record CreateAiChatRequest(string? Title = null, string? OrganizationId = null, JsonElement? Messages = null);
public record UpdateAiChatRequest(string? Title = null, JsonElement? Messages = null);
public record AiChatDto(string Id, string? Title, string? OrganizationId, JsonElement? Messages, DateTime CreatedAt, DateTime? UpdatedAt);
public record AiChatListResponse(List<AiChatDto> Chats);
