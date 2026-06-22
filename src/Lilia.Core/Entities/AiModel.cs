namespace Lilia.Core.Entities;

/// <summary>
/// One selectable AI model in the data-driven model catalog (<c>ai_models</c>).
///
/// Replaces hardcoded model ids: the catalog is the source of truth for which
/// models exist, what they cost (credit multipliers, used from Phase 2), which
/// membership may select them, and what they can do (attachments/vision). Loaded
/// into an in-memory map at startup — same pattern as <see cref="LatexToken"/> /
/// <see cref="LatexUnicodeChar"/> — and exposed to the editor via GET /api/ai/models.
///
/// Phase 1 introduces the catalog + provider routing; credit fields are stored
/// now and enforced later (Phase 2 metering).
/// </summary>
public class AiModel
{
    /// <summary>Provider model id, e.g. <c>claude-sonnet-4-6</c>. Primary key.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>anthropic | openai | google.</summary>
    public string Provider { get; set; } = "anthropic";

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>fast | default | premium — for grouping in the picker.</summary>
    public string TierLabel { get; set; } = "default";

    /// <summary>free | pro | team — the lowest membership that may select it.</summary>
    public string MinMembership { get; set; } = "pro";

    /// <summary>Credits charged per 1k input / output tokens (Phase 2 metering).</summary>
    public decimal CreditInPerKTok { get; set; }
    public decimal CreditOutPerKTok { get; set; }

    public int ContextWindow { get; set; }

    /// <summary>Hard cap on output tokens per call (also tier-capped).</summary>
    public int MaxOutput { get; set; }

    public bool SupportsAttachments { get; set; }
    public bool SupportsVision { get; set; }
    public bool PromptCache { get; set; }

    /// <summary>Exactly one row should be the default (used when no model is chosen).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Kill switch — disabled models never appear or resolve.</summary>
    public bool Enabled { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
