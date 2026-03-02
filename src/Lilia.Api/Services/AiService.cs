using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

public class AiService : IAiService
{
    private readonly IChatClient _chatClient;
    private readonly LiliaDbContext _db;
    private readonly AiOptions _options;
    private readonly ILogger<AiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AiService(
        IChatClient chatClient,
        LiliaDbContext db,
        IOptions<AiOptions> options,
        ILogger<AiService> logger)
    {
        _chatClient = chatClient;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    private string GetModelForFeature(string feature)
    {
        return _options.Models.TryGetValue(feature, out var model) ? model : _options.DefaultModel;
    }

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
        }
        if (text.EndsWith("```"))
            text = text[..^3];
        return text.Trim();
    }

    public async Task<GenerateBlockResponse> GenerateBlockAsync(string prompt, GenerateBlockContext? context = null)
    {
        var model = GetModelForFeature("generate");
        var contextParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context?.DocumentTitle))
            contextParts.Add($"Document title: {context.DocumentTitle}");
        if (!string.IsNullOrWhiteSpace(context?.SurroundingText))
            contextParts.Add($"Surrounding text: {context.SurroundingText}");
        var userMessage = contextParts.Count > 0
            ? $"Document context:\n{string.Join("\n", contextParts)}\n\nGenerate: {prompt}"
            : prompt;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiPrompts.GenerateBlock),
            new(ChatRole.User, userMessage),
        };

        _logger.LogInformation("Generating block with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 2048 });
        var raw = StripMarkdownFences(response.Text);
        var json = JsonSerializer.Deserialize<JsonElement>(raw, JsonOptions);

        var type = json.GetProperty("type").GetString()!;
        var content = json.GetProperty("content");
        return new GenerateBlockResponse(type, content);
    }

    public async Task<ImproveTextResponse> ImproveTextAsync(string text, string action)
    {
        var model = GetModelForFeature("improve");
        var actionPrompt = AiPrompts.ImproveActions.GetValueOrDefault(action, AiPrompts.ImproveActions["improve"]);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiPrompts.ImproveText),
            new(ChatRole.User, $"{actionPrompt}\n\n{text}"),
        };

        _logger.LogInformation("Improving text with action {Action}, model {Model}", action, model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 4096 });
        return new ImproveTextResponse(response.Text.Trim());
    }

    public async Task<SuggestEquationResponse> SuggestEquationAsync(string description)
    {
        var model = GetModelForFeature("equation");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiPrompts.SuggestEquation),
            new(ChatRole.User, description),
        };

        _logger.LogInformation("Suggesting equation with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 1024 });
        var latex = response.Text.Trim();

        var block = JsonSerializer.SerializeToElement(new
        {
            type = "equation",
            content = new { latex, equationMode = "display" },
        }, JsonOptions);

        return new SuggestEquationResponse(latex, block);
    }

    public async Task<GrammarCheckResponse> GrammarCheckAsync(string text)
    {
        var model = GetModelForFeature("grammar");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiPrompts.GrammarCheck),
            new(ChatRole.User, text),
        };

        _logger.LogInformation("Grammar check with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 4096 });
        var raw = StripMarkdownFences(response.Text);
        var suggestions = JsonSerializer.Deserialize<List<GrammarSuggestion>>(raw, JsonOptions) ?? [];
        return new GrammarCheckResponse(suggestions);
    }

    public async Task<CitationCheckResponse> CitationCheckAsync(string text)
    {
        var model = GetModelForFeature("citation");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiPrompts.CitationCheck),
            new(ChatRole.User, text),
        };

        _logger.LogInformation("Citation check with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 4096 });
        var raw = StripMarkdownFences(response.Text);
        var suggestions = JsonSerializer.Deserialize<List<CitationSuggestion>>(raw, JsonOptions) ?? [];
        return new CitationCheckResponse(suggestions);
    }

    public async Task<GenerateAbstractResponse> GenerateAbstractAsync(string title, string content)
    {
        var model = GetModelForFeature("abstract");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiPrompts.GenerateAbstract),
            new(ChatRole.User, $"Title: {title}\n\nContent:\n{content}"),
        };

        _logger.LogInformation("Generating abstract with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 2048 });
        return new GenerateAbstractResponse(response.Text.Trim());
    }

    // --- Chat CRUD ---

    public async Task<AiChatDto> CreateChatAsync(string userId, CreateAiChatRequest request)
    {
        var chat = new AiChat
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            OrganizationId = request.OrganizationId,
            Title = request.Title,
            Messages = request.Messages.HasValue
                ? JsonDocument.Parse(request.Messages.Value.GetRawText())
                : null,
            CreatedAt = DateTime.UtcNow,
        };

        _db.AiChats.Add(chat);
        await _db.SaveChangesAsync();
        return ToDto(chat);
    }

    public async Task<AiChatListResponse> GetChatsAsync(string userId, string? organizationId = null)
    {
        var query = _db.AiChats.Where(c => c.UserId == userId);
        if (organizationId != null)
            query = query.Where(c => c.OrganizationId == organizationId);

        var chats = await query.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt).ToListAsync();
        return new AiChatListResponse(chats.Select(ToDto).ToList());
    }

    public async Task<AiChatDto?> GetChatAsync(string userId, string id)
    {
        var chat = await _db.AiChats.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        return chat != null ? ToDto(chat) : null;
    }

    public async Task<AiChatDto?> UpdateChatAsync(string userId, string id, UpdateAiChatRequest request)
    {
        var chat = await _db.AiChats.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (chat == null) return null;

        if (request.Title != null)
            chat.Title = request.Title;
        if (request.Messages.HasValue)
        {
            chat.Messages?.Dispose();
            chat.Messages = JsonDocument.Parse(request.Messages.Value.GetRawText());
        }
        chat.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToDto(chat);
    }

    public async Task<bool> DeleteChatAsync(string userId, string id)
    {
        var chat = await _db.AiChats.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (chat == null) return false;

        _db.AiChats.Remove(chat);
        await _db.SaveChangesAsync();
        return true;
    }

    private static AiChatDto ToDto(AiChat chat)
    {
        JsonElement? messages = chat.Messages != null
            ? chat.Messages.RootElement.Clone()
            : null;

        return new AiChatDto(
            chat.Id,
            chat.Title,
            chat.OrganizationId,
            messages,
            chat.CreatedAt,
            chat.UpdatedAt);
    }
}
