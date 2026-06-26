using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lilia.Api.Models.AiArchitect;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

/// <summary>
/// "Ask Lilia" generation dispatch. Routes a natural-language message to a skill
/// (<see cref="IAskLiliaRouter"/>), assembles the system prompt = proficiency
/// preamble + the skill's SKILL.md guidance + a uniform output note, and runs it
/// through the same governed path as the architect (key gate, entitlement/budget,
/// model catalog, ai_requests audit, credit metering).
///
/// Text mode: the reply carries the artifact (LML / BibTeX / LaTeX in fenced
/// blocks the author copies or applies). The Document Architect keeps its dedicated
/// structured-BlockOps endpoint for in-place document editing; structured ops for
/// the other block-emitting skills are a later refinement.
/// </summary>
public interface IAskLiliaService
{
    Task<AskLiliaResult> AskAsync(string userId, AskLiliaRequest request, CancellationToken ct = default);
}

public sealed record AskLiliaRequest(string Message, string? Proficiency = null, string? Model = null);

public sealed record AskLiliaResponse(
    string SkillId, string SkillName, string Reply,
    AiArchitectUsage Usage, AiArchitectBalance? Balance, int CreditsUsed);

public sealed record AskLiliaResult(bool Locked, string? Reason, string? Message, AskLiliaResponse? Response)
{
    public static AskLiliaResult Lock(string reason, string message) => new(true, reason, message, null);
    public static AskLiliaResult Ok(AskLiliaResponse r) => new(false, null, null, r);
}

public sealed class AskLiliaService : IAskLiliaService
{
    private readonly IChatClient _chatClient;
    private readonly IEntitlementService _entitlement;
    private readonly IAiCatalogService _catalog;
    private readonly IAskLiliaRouter _router;
    private readonly LiliaDbContext _context;
    private readonly AiOptions _options;
    private readonly ILogger<AskLiliaService> _logger;
    private readonly bool _useAi;
    private readonly bool _enabled;
    private readonly bool _enforceCredits;

    private const int MaxOutputTokens = 4096;

    private const string OutputNote = """
        OUTPUT — respond conversationally to the author. Put any LML, BibTeX, or LaTeX you produce
        in fenced code blocks they can copy into Lilia (or apply). Lead with the artifact; keep prose
        short. Follow the AUTHOR LEVEL guidance above. Never fabricate citations, numbers, datasets,
        or results — keep specifics the author must supply generic and flagged.
        """;

    public AskLiliaService(
        IChatClient chatClient,
        IEntitlementService entitlement,
        IAiCatalogService catalog,
        IAskLiliaRouter router,
        LiliaDbContext context,
        IOptions<AiOptions> options,
        IConfiguration configuration,
        ILogger<AskLiliaService> logger)
    {
        _chatClient = chatClient;
        _entitlement = entitlement;
        _catalog = catalog;
        _router = router;
        _context = context;
        _options = options.Value;
        _logger = logger;
        _useAi = !string.IsNullOrEmpty(_options.Anthropic.ApiKey) && _options.Anthropic.ApiKey != "sk-placeholder";
        _enabled = configuration.GetValue("AI:Enabled", true);
        _enforceCredits = configuration.GetValue("AI:EnforceCredits", false);
    }

    // ai_requests.purpose is CHECK-constrained — map each skill to a member of the
    // closed vocabulary, defaulting to 'other'.
    private static string PurposeFor(string skillId) => skillId switch
    {
        "lilia-citations" => "suggest_bibliography",
        "lilia-compile-doctor" => "fix_latex",
        "lilia-polish" => "rephrase",
        "lilia-journal" => "suggest_headings",
        "lilia-document-architect" => "expand_outline",
        _ => "other",
    };

    public async Task<AskLiliaResult> AskAsync(string userId, AskLiliaRequest request, CancellationToken ct = default)
    {
        // ── gate ──────────────────────────────────────────────────────────
        if (!_enabled) return AskLiliaResult.Lock("disabled", "AI is currently disabled.");
        if (!_useAi) return AskLiliaResult.Lock("no-key", "AI is not configured.");

        try
        {
            await _entitlement.EnsureQuotaAsync(userId, QuotaResource.AiCredits, delta: 1, ct);
        }
        catch (QuotaExceededException)
        {
            if (_enforceCredits)
                return AskLiliaResult.Lock("over-budget", "You've used all your AI credits for this period.");
            _logger.LogInformation("[AskLilia] User {UserId} over budget; credit enforcement off — allowing", userId);
        }

        // ── route + prompt ────────────────────────────────────────────────
        var route = _router.Route(request.Message);
        var skill = _router.Get(route.SkillId);
        var level = ProficiencyGuidance.Parse(request.Proficiency);

        var guidance = AiSkillGuidance.Get(skill.Id);
        var system = new StringBuilder()
            .AppendLine(ProficiencyGuidance.For(level)).AppendLine()
            .AppendLine(string.IsNullOrEmpty(guidance) ? $"You are the {skill.Name} skill for Lilia. {skill.WhenToUse}" : guidance)
            .AppendLine().AppendLine(OutputNote)
            .ToString();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, request.Message),
        };

        // ── model resolution (catalog default; honour a tier-allowed override) ─
        var model = _catalog.DefaultModelId();
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            var plan = await _entitlement.GetActivePlanAsync(userId, ct);
            var slug = plan?.Slug?.ToLowerInvariant();
            var tier = slug is "pro" or "team" ? slug : "free";
            if (_catalog.IsAllowedFor(request.Model, tier)) model = request.Model;
        }

        // ── audit → call → meter ──────────────────────────────────────────
        var aiRequestId = await PersistPendingAsync(userId, PurposeFor(skill.Id), model, messages, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[AskLilia] skill={Skill} user={UserId} model={Model}", skill.Id, userId, model);

            var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
            {
                ModelId = model,
                MaxOutputTokens = MaxOutputTokens,
                Temperature = 0.4f,
            }, ct);

            var reply = (response.Text ?? string.Empty).Trim();
            var inputTokens = (int?)response.Usage?.InputTokenCount ?? 0;
            var outputTokens = (int?)response.Usage?.OutputTokenCount ?? 0;
            var costUsd = AiArchitectPricing.ComputeCostUsd(model, inputTokens, outputTokens);

            await MarkAsync(aiRequestId, "success", null, inputTokens, outputTokens, (int)sw.ElapsedMilliseconds, ct);

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
                _logger.LogWarning(ex, "[AskLilia] Credit debit failed for user {UserId}", userId);
            }

            var result = new AskLiliaResponse(
                skill.Id, skill.Name, reply,
                new AiArchitectUsage(inputTokens, outputTokens, costUsd, credits), balance, creditsUsed);
            return AskLiliaResult.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AskLilia] AI call failed for user {UserId} skill {Skill}", userId, skill.Id);
            await MarkAsync(aiRequestId, "error", Truncate(ex.Message, 500), 0, 0, (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
    }

    private async Task<Guid> PersistPendingAsync(string userId, string purpose, string model, List<ChatMessage> messages, CancellationToken ct)
    {
        var row = new AiRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DocumentId = null,
            BlockId = null,
            Purpose = purpose,
            Provider = "anthropic",
            Model = model,
            PromptHash = HashPrompt(string.Join("\n", messages.Select(m => m.Text))),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        _context.AiRequests.Add(row);
        await _context.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task MarkAsync(Guid id, string status, string? errorMessage, int promptTokens, int completionTokens, int latencyMs, CancellationToken ct)
        => await _context.AiRequests.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.ErrorMessage, errorMessage)
                .SetProperty(r => r.PromptTokens, promptTokens)
                .SetProperty(r => r.CompletionTokens, completionTokens)
                .SetProperty(r => r.LatencyMs, latencyMs)
                .SetProperty(r => r.CompletedAt, DateTime.UtcNow), ct);

    private static string HashPrompt(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
