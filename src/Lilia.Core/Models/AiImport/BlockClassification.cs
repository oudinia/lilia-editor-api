namespace Lilia.Core.Models.AiImport;

/// <summary>
/// Result of AI-powered block type classification.
/// </summary>
public record BlockClassification(
    /// <summary>The block ID being classified (null for standalone classification).</summary>
    string? BlockId,

    /// <summary>The current/original block type.</summary>
    string CurrentType,

    /// <summary>The AI-suggested block type.</summary>
    string SuggestedType,

    /// <summary>Confidence score between 0 and 1.</summary>
    double Confidence,

    /// <summary>Human-readable reasoning for the classification.</summary>
    string Reasoning
)
{
    /// <summary>Whether the AI suggests a different type than the current one.</summary>
    public bool SuggestsChange => !string.Equals(CurrentType, SuggestedType, StringComparison.OrdinalIgnoreCase);
}
