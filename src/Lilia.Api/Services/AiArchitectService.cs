using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lilia.Api.Models.AiArchitect;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

/// <summary>
/// The hosted, paid, in-app "Document Architect": a conversational agent that
/// reads a document's current blocks and proposes typed-block operations the
/// editor can apply. It NEVER mutates the document — it only reads context and
/// returns proposed ops, which keeps it stateless and restart / multi-instance
/// safe. Conversation history is client-held (sent on every call).
///
/// Follows the AiAssistantService pattern: inject <see cref="IChatClient"/>,
/// gate on a usable API key, call GetResponseAsync with the configured model.
/// Spend persists to ai_requests + ai_credit_ledger via EntitlementService.
/// </summary>
public class AiArchitectService : IAiArchitectService
{
    private readonly IChatClient _chatClient;
    private readonly IDocumentService _documentService;
    private readonly IEntitlementService _entitlement;
    private readonly IAiCatalogService _catalog;
    private readonly LiliaDbContext _context;
    private readonly AiOptions _options;
    private readonly ILogger<AiArchitectService> _logger;
    private readonly bool _useAi;
    private readonly bool _enabled;

    // ai_requests.purpose is CHECK-constrained; "expand_outline" is the closest
    // member of the closed vocabulary for structural architect work.
    private const string Purpose = "expand_outline";

    private const int MaxOutputTokens = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AiArchitectService(
        IChatClient chatClient,
        IDocumentService documentService,
        IEntitlementService entitlement,
        IAiCatalogService catalog,
        LiliaDbContext context,
        IOptions<AiOptions> options,
        IConfiguration configuration,
        ILogger<AiArchitectService> logger)
    {
        _chatClient = chatClient;
        _documentService = documentService;
        _entitlement = entitlement;
        _catalog = catalog;
        _context = context;
        _options = options.Value;
        _logger = logger;

        _useAi = !string.IsNullOrEmpty(_options.Anthropic.ApiKey)
                 && _options.Anthropic.ApiKey != "sk-placeholder";

        _enabled = configuration.GetValue("AI:Enabled", true);
        // Observe-only by default: spend is recorded but never blocks. Flip
        // AI:EnforceCredits=true once per-plan allowances are set.
        _enforceCredits = configuration.GetValue("AI:EnforceCredits", false);
    }

    private readonly bool _enforceCredits;

    public async Task<AiArchitectOutcome> ArchitectAsync(
        string userId, AiArchitectRequest request, CancellationToken ct = default)
    {
        // ── 1. GATE ───────────────────────────────────────────────────────
        if (!_enabled)
            return AiArchitectOutcome.Lock("disabled", "AI is currently disabled.");

        if (!_useAi)
            return AiArchitectOutcome.Lock("no-key", "AI is not configured.");

        // Entitlement / budget. EnsureQuotaAsync(AiCredits) throws when the
        // user's monthly AI credit balance is exhausted (over budget). A user
        // with no plan data at all falls through (fail-open, logged elsewhere).
        try
        {
            await _entitlement.EnsureQuotaAsync(userId, QuotaResource.AiCredits, delta: 1, ct);
        }
        catch (QuotaExceededException)
        {
            // Observe-only: record-but-don't-block until allowances are set and
            // AI:EnforceCredits is flipped on. Logged so over-budget is visible.
            if (_enforceCredits)
                return AiArchitectOutcome.Lock("over-budget", "You've used all your AI credits for this period.");
            _logger.LogInformation("[AiArchitect] User {UserId} over budget, but credit enforcement is off (observe-only) — allowing", userId);
        }

        // Resolve the document for context. A from-scratch draft (no id, the
        // sentinel "new", or an unparseable id) is a valid first-run: the
        // architect proposes a structure from nothing and the editor applies
        // accepted ops to a freshly-minted doc. We never lock on a missing
        // document — only key / entitlement / budget gate access.
        Guid? documentId = null;
        DocumentDto? document = null;
        if (Guid.TryParse(request.DocumentId, out var parsedId))
        {
            document = await _documentService.GetDocumentAsync(parsedId, userId);
            if (document is not null)
                documentId = parsedId;
        }

        // ── 2. CONTEXT ────────────────────────────────────────────────────
        var context = document is not null
            ? BuildDocumentContext(document)
            : AiArchitectPrompts.NewDraftContext;

        // ── 3. MESSAGES ───────────────────────────────────────────────────
        var systemPrompt = AiArchitectPrompts.BuildSystemPrompt(context);
        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        foreach (var m in request.Messages)
        {
            var role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.Assistant
                : ChatRole.User;
            messages.Add(new ChatMessage(role, m.Content ?? string.Empty));
        }

        // Resolve the model: honour a valid, tier-allowed request override;
        // otherwise the catalog default (Sonnet 4.6). Keeps free users off
        // pro-gated models even if the client sends one.
        var model = _catalog.DefaultModelId();
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            var plan = await _entitlement.GetActivePlanAsync(userId, ct);
            var slug = plan?.Slug?.ToLowerInvariant();
            var tier = slug is "pro" or "team" ? slug : "free";
            if (_catalog.IsAllowedFor(request.Model, tier))
                model = request.Model;
            else
                _logger.LogInformation(
                    "[AiArchitect] Model {Model} not allowed for tier {Tier}; using default {Default}",
                    request.Model, tier, model);
        }

        // ── 4-6. CALL + METER (audit row persisted before the network call) ─
        var aiRequestId = await PersistPendingAsync(userId, documentId, model, messages, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation(
                "[AiArchitect] Architecting doc {DocumentId} for user {UserId} with model {Model}",
                documentId, userId, model);

            var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
            {
                ModelId = model,
                MaxOutputTokens = MaxOutputTokens,
                Temperature = 0.3f,
            }, ct);

            var (reply, operations) = ParseResponse(response.Text ?? string.Empty);

            var inputTokens = (int?)response.Usage?.InputTokenCount ?? 0;
            var outputTokens = (int?)response.Usage?.OutputTokenCount ?? 0;
            var costUsd = AiArchitectPricing.ComputeCostUsd(model, inputTokens, outputTokens);

            await MarkAsync(aiRequestId, "success", null, inputTokens, outputTokens, (int)sw.ElapsedMilliseconds, ct);

            // Debit the credit ledger (model-weighted). Best-effort: a metering
            // failure must not lose the user's result, but it is logged.
            AiArchitectBalance? balance = null;
            var credits = 0;
            var creditsUsed = 0;
            try
            {
                credits = await _entitlement.RecordAiSpendAsync(userId, model, inputTokens, outputTokens, aiRequestId, ct);
                var creditsLeft = await _entitlement.GetAiCreditBalanceAsync(userId, ct);
                balance = new AiArchitectBalance(AiArchitectPricing.CreditsToUsd(creditsLeft));
                creditsUsed = await _entitlement.GetAiCreditsConsumedAsync(userId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiArchitect] Credit debit failed for user {UserId}", userId);
            }

            var result = new AiArchitectResponse(
                reply,
                operations,
                new AiArchitectUsage(inputTokens, outputTokens, costUsd, credits),
                balance,
                creditsUsed);

            return AiArchitectOutcome.Ok(result);
        }
        catch (Exception ex)
        {
            // No silent failures: log + persist the failure on the audit row.
            _logger.LogError(ex, "[AiArchitect] AI call failed for user {UserId} doc {DocumentId}", userId, documentId);
            await MarkAsync(aiRequestId, "error", Truncate(ex.Message, 500), 0, 0, (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Document context
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialize the document's current blocks into a compact typed list for
    /// the system prompt. Each line carries the block id so the model can
    /// reference existing blocks in edit/move/remove ops.
    /// </summary>
    internal static string BuildDocumentContext(DocumentDto document)
    {
        var sb = new StringBuilder();
        sb.Append("Title: ").AppendLine(string.IsNullOrWhiteSpace(document.Title) ? "(untitled)" : document.Title);
        if (!string.IsNullOrWhiteSpace(document.DocumentCategory))
            sb.Append("Document kind: ").AppendLine(document.DocumentCategory);
        if (!string.IsNullOrWhiteSpace(document.LatexDocumentClass))
            sb.Append("LaTeX class: ").AppendLine(document.LatexDocumentClass);

        var blocks = (document.Blocks ?? new List<BlockDto>())
            .OrderBy(b => b.SortOrder)
            .ToList();

        if (blocks.Count == 0)
        {
            sb.AppendLine("Current blocks: (none — the document is empty)");
            return sb.ToString();
        }

        sb.AppendLine($"Current blocks ({blocks.Count}), in order:");
        foreach (var b in blocks)
        {
            var summary = SummarizeContent(b.Content);
            sb.Append("- id=").Append(b.Id)
              .Append(" type=").Append(b.Type)
              .Append(' ').AppendLine(summary);
        }
        return sb.ToString();
    }

    private static string SummarizeContent(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
            return string.Empty;

        // Prefer a human-readable field if present, else compact the JSON.
        foreach (var field in new[] { "text", "caption", "title", "latex", "code" })
        {
            if (content.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString() ?? string.Empty;
                return $"\"{Truncate(s.Replace('\n', ' '), 120)}\"";
            }
        }

        var raw = content.GetRawText();
        return Truncate(raw.Replace('\n', ' '), 120);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Response parsing — reply + operations
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the model's reply into a natural-language string + a list of
    /// block operations. The model is instructed to return a single JSON
    /// object {"reply": "...", "operations": [...]}; Claude often wraps it in
    /// prose / fences, so we extract the outermost JSON object robustly. If
    /// nothing parses, the whole text becomes the reply with no operations.
    /// </summary>
    internal static (string Reply, List<BlockOp> Operations) ParseResponse(string raw)
    {
        var json = ExtractJsonObject(raw);
        if (json is not null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ArchitectModelOutput>(json, JsonOptions);
                if (parsed is not null)
                {
                    var reply = string.IsNullOrWhiteSpace(parsed.Reply) ? raw.Trim() : parsed.Reply.Trim();
                    var ops = (parsed.Operations ?? new List<BlockOp>())
                        .Where(IsValidOp)
                        .ToList();
                    return (reply, ops);
                }
            }
            catch (JsonException)
            {
                // Fall through to the plain-text fallback below.
            }
        }

        return (raw.Trim(), new List<BlockOp>());
    }

    private static bool IsValidOp(BlockOp op)
    {
        if (op is null) return false;
        var validOp = op.Op is "add" or "edit" or "move" or "remove";
        if (!validOp) return false;
        // add/edit must carry a payload with a valid in-vocabulary type.
        if (op.Op is "add" or "edit")
        {
            if (op.Block is null) return false;
            if (!AiArchitectPrompts.ValidBlockTypes.Contains(op.Block.Type)) return false;
        }
        return true;
    }

    /// <summary>
    /// Extract the outermost balanced JSON object from arbitrary model text
    /// (handles ```json fences and surrounding prose). Returns null if no
    /// balanced object is found.
    /// </summary>
    internal static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return text.Substring(start, i - start + 1);
                    break;
            }
        }
        return null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Audit row persistence (mirrors AiOrchestrator)
    // ──────────────────────────────────────────────────────────────────────

    private async Task<Guid> PersistPendingAsync(
        string userId, Guid? documentId, string model, List<ChatMessage> messages, CancellationToken ct)
    {
        var promptHash = HashPrompt(string.Join("\n", messages.Select(m => m.Text)));
        var row = new AiRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DocumentId = documentId,
            BlockId = null,
            Purpose = Purpose,
            Provider = "anthropic",
            Model = model,
            PromptHash = promptHash,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        _context.AiRequests.Add(row);
        await _context.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task MarkAsync(
        Guid id, string status, string? errorMessage,
        int promptTokens, int completionTokens, int latencyMs, CancellationToken ct)
    {
        await _context.AiRequests
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.ErrorMessage, errorMessage)
                .SetProperty(r => r.PromptTokens, promptTokens)
                .SetProperty(r => r.CompletionTokens, completionTokens)
                .SetProperty(r => r.LatencyMs, latencyMs)
                .SetProperty(r => r.CompletedAt, DateTime.UtcNow),
                ct);
    }

    private static string HashPrompt(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private record ArchitectModelOutput(string? Reply, List<BlockOp>? Operations);
}
