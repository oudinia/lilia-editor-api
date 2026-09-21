using System.Text.Json;

namespace Lilia.Core.DTOs;

// --- Generate Block ---
public record GenerateBlockContext(string? DocumentTitle = null, string? SurroundingText = null);
public record GenerateBlockRequest(string Prompt, GenerateBlockContext? Context = null);

/// <summary>Revise a block the author already has. Block-generic on purpose:
/// the table tool is the first caller, not the only one.</summary>
public record ReviseBlockRequest(
    string Type,
    JsonElement Content,
    string Instruction,
    /// <summary>Compile the result before returning it, and retry once with the
    /// compiler's error if it does not build. Costs a round trip; worth it for
    /// anything going straight into a document.</summary>
    bool Verify = true,
    string? Engine = null);

/// <summary>What the model came back with, before anyone has compiled it.</summary>
public record ReviseBlockResult(string Type, JsonElement Content, string Note);

/// <summary>The revised block, and whether it was proven to compile. A false
/// <paramref name="Verified"/> with a block attached means "here it is, but it
/// did not build" — the caller decides, rather than getting nothing.</summary>
public record ReviseBlockResponse(
    string Type,
    JsonElement Content,
    string Note,
    bool Verified,
    string? Error = null,
    int Attempts = 1);
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
