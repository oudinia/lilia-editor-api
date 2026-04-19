using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>
/// Enforces the opt-in gate, redaction, rate-limit, and audit-log policy for
/// every outbound AI call. New AI features inject this, not IChatClient /
/// IAiService, so the invariants can't be forgotten.
///
/// Rate-limit policy (v1): per-user hourly cap (default 60/h, configurable
/// via AI:RateLimitPerHour). Intentionally simple — a single window. Will
/// become credit-based once the entitlement layer ships.
/// </summary>
public class AiOrchestrator : IAiOrchestrator
{
    private readonly LiliaDbContext _context;
    private readonly IRedactionService _redaction;
    private readonly IChatClient _chatClient;
    private readonly IEntitlementService? _entitlement;
    private readonly ILogger<AiOrchestrator> _logger;
    private readonly int _rateLimitPerHour;

    public AiOrchestrator(
        LiliaDbContext context,
        IRedactionService redaction,
        IChatClient chatClient,
        IConfiguration config,
        ILogger<AiOrchestrator> logger,
        IEntitlementService? entitlement = null)
    {
        _context = context;
        _redaction = redaction;
        _chatClient = chatClient;
        _entitlement = entitlement;
        _logger = logger;
        _rateLimitPerHour = int.TryParse(config["AI:RateLimitPerHour"], out var n) && n > 0 ? n : 60;
    }

    public async Task<AiOrchestratorResult> RunAsync(AiOrchestratorRequest request, CancellationToken ct = default)
    {
        // 1. Opt-in gate: if the request targets a specific document, that
        //    document must have AiEnabled=true. No document = global tool
        //    like "summarise this text" — allowed by default, but still
        //    logged and rate-limited.
        if (request.DocumentId.HasValue)
        {
            var enabled = await _context.Documents
                .AsNoTracking()
                .Where(d => d.Id == request.DocumentId.Value)
                .Select(d => (bool?)d.AiEnabled)
                .FirstOrDefaultAsync(ct);
            if (enabled != true)
            {
                return new AiOrchestratorResult(Guid.Empty, "disabled",
                    null, "AI is disabled for this document.", 0, 0, 0);
            }
        }

        // 2. Rate limit: count the user's requests in the last hour.
        var windowStart = DateTime.UtcNow.AddHours(-1);
        var recentCount = await _context.AiRequests
            .AsNoTracking()
            .Where(r => r.UserId == request.UserId && r.CreatedAt >= windowStart)
            .CountAsync(ct);
        if (recentCount >= _rateLimitPerHour)
        {
            var throttled = await PersistAsync(request, "pending", promptHash: "", redactionSummary: null, ct);
            await MarkAsync(throttled, "rate_limited", errorMessage: $"Exceeded {_rateLimitPerHour} requests/hour", ct: ct);
            return new AiOrchestratorResult(throttled, "rate_limited", null,
                $"Rate limit exceeded — try again later ({recentCount}/{_rateLimitPerHour} in the last hour).", 0, 0, 0);
        }

        // 3. Redact the prompt before it leaves the process.
        var redacted = _redaction.Redact(request.UserPrompt);

        // 4. If nothing meaningful is left after redaction, refuse. Threshold:
        //    less than 16 non-token characters remaining.
        var meaningful = System.Text.RegularExpressions.Regex.Replace(redacted.Text, @"\[\[[A-Z_]+\d+\]\]", "").Trim().Length;
        if (meaningful < 16)
        {
            var refused = await PersistAsync(request, "pending", HashPrompt(redacted.Text),
                redactionSummary: redacted.Summary, ct);
            await MarkAsync(refused, "redacted_refused", errorMessage: "Prompt was mostly PII after redaction.", ct: ct);
            return new AiOrchestratorResult(refused, "redacted_refused", null,
                "Prompt contained mostly personal information; nothing to send.",
                0, 0, redacted.TotalReplacements);
        }

        // 5. Persist the pending audit row BEFORE the network call so we
        //    still have a record if the call hangs or crashes.
        var promptHash = HashPrompt(redacted.Text);
        var requestId = await PersistAsync(request, "pending", promptHash,
            redactionSummary: redacted.Summary, ct);

        // 6. Make the call. IChatClient is Microsoft.Extensions.AI's abstraction
        //    over providers — already wired to Anthropic in Program.cs.
        var sw = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrEmpty(request.SystemPrompt))
                messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
            messages.Add(new ChatMessage(ChatRole.User, redacted.Text));

            var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
            {
                ModelId = request.Model,
                MaxOutputTokens = request.MaxTokens,
                Temperature = (float)request.Temperature,
            }, ct);

            var text = response.Text ?? string.Empty;
            var promptTokens = (int?)response.Usage?.InputTokenCount ?? 0;
            var completionTokens = (int?)response.Usage?.OutputTokenCount ?? 0;

            await MarkAsync(requestId, "success", errorMessage: null,
                promptTokens: promptTokens, completionTokens: completionTokens,
                latencyMs: (int)sw.ElapsedMilliseconds, ct);

            // Debit the AI credit ledger for this spend. Best-effort — if
            // the entitlement service isn't wired (e.g. older DI) the call
            // is a no-op. Credits use ceil(tokens / 1000).
            if (_entitlement != null)
            {
                try
                {
                    await _entitlement.RecordAiSpendAsync(request.UserId, promptTokens + completionTokens, requestId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI credit debit failed for user {UserId}", request.UserId);
                }
            }

            return new AiOrchestratorResult(requestId, "success", text, null,
                promptTokens, completionTokens, redacted.TotalReplacements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI call failed for user {UserId} purpose {Purpose}", request.UserId, request.Purpose);
            await MarkAsync(requestId, "error", errorMessage: Truncate(ex.Message, 500),
                latencyMs: (int)sw.ElapsedMilliseconds, ct: ct);
            return new AiOrchestratorResult(requestId, "error", null, ex.Message, 0, 0, redacted.TotalReplacements);
        }
    }

    private async Task<Guid> PersistAsync(
        AiOrchestratorRequest request,
        string status,
        string promptHash,
        System.Text.Json.JsonDocument? redactionSummary,
        CancellationToken ct)
    {
        var row = new AiRequest
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            DocumentId = request.DocumentId,
            BlockId = request.BlockId,
            Purpose = request.Purpose,
            Provider = "anthropic",
            Model = request.Model,
            PromptHash = promptHash,
            RedactionSummary = redactionSummary,
            Status = status,
            CreatedAt = DateTime.UtcNow,
        };
        _context.AiRequests.Add(row);
        await _context.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task MarkAsync(
        Guid id,
        string status,
        string? errorMessage = null,
        int promptTokens = 0,
        int completionTokens = 0,
        int? latencyMs = null,
        CancellationToken ct = default)
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
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
