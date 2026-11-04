using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Audit log for every AI call the platform makes on behalf of a user.
///
/// Written BEFORE the AI request fires (with <see cref="Status"/>="pending"),
/// updated in-place with token counts and outcome. Purpose: billing,
/// rate-limiting, abuse triage, and a user-visible "AI activity" feed.
///
/// The prompt itself is NOT stored — only a hash and a redaction summary,
/// because prompts can embed user document content including PII. If we
/// need to debug a specific prompt we rehydrate from the block + redaction
/// rules; we don't keep raw prompts on disk.
/// </summary>
public class AiRequest
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public string? BlockId { get; set; }

    /// <summary>
    /// Logical reason this request was made. Closed vocabulary (CHECK in DB):
    /// rephrase | summarise | suggest_headings | suggest_bibliography |
    /// fix_latex | expand_outline | review_finding | redact_pii | other.
    /// </summary>
    public string Purpose { get; set; } = "other";

    /// <summary>Provider slug — anthropic | openai | local.</summary>
    public string Provider { get; set; } = "anthropic";

    /// <summary>Model identifier, e.g. "claude-opus-5" / "claude-sonnet-5".</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>SHA-256 of the outgoing prompt AFTER redaction.</summary>
    public string PromptHash { get; set; } = string.Empty;

    /// <summary>
    /// JSON describing which redaction rules fired and how many matches,
    /// e.g. {"email":3,"phone":1,"linkedin_url":2}. Never contains the
    /// actual matched strings.
    /// </summary>
    public JsonDocument? RedactionSummary { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }

    // What an Ask Lilia turn cost, to measure it (4 Nov 2026): before, only the credits per message were kept,
    // so prompt size and the cache's effect could not be seen. PromptTokens counts every input token, cached or not.

    /// <summary>The Ask Lilia skill that answered (e.g. lilia-polish); null for other AI calls.</summary>
    public string? Skill { get; set; }

    /// <summary>Input tokens read from the prompt cache (billed at a tenth).</summary>
    public int CacheReadTokens { get; set; }

    /// <summary>Input tokens written to the prompt cache (billed at 1.25×).</summary>
    public int CacheWriteTokens { get; set; }

    /// <summary>Model calls in the turn: one, plus one per round of tool use. 0 where not counted (maths, figures).</summary>
    public int Rounds { get; set; }

    /// <summary>The largest single call's input: above 100,000 tokens Haiku 5.5 bills five times its base rate. 0 where not counted.</summary>
    public int LargestPromptTokens { get; set; }

    /// <summary>The turn's cost in USD at list prices, web searches included.</summary>
    public decimal? CostUsd { get; set; }

    /// <summary>
    /// Lifecycle: pending | success | error | rate_limited | redacted_refused.
    /// "redacted_refused" means the redaction step stripped so much the
    /// request wasn't worth sending.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>Error message (truncated) when Status=error.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Latency in milliseconds from start to first response byte.</summary>
    public int? LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public virtual Document? Document { get; set; }
}
