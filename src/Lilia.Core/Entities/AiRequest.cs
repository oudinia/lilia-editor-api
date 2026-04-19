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

    /// <summary>Model identifier, e.g. "claude-opus-4-7" / "claude-sonnet-4-6".</summary>
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
