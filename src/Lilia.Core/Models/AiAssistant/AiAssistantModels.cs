namespace Lilia.Core.Models.AiAssistant;

/// <summary>
/// Result of generating LaTeX from a natural language description.
/// </summary>
public record MathGenerationResult(
    /// <summary>The generated LaTeX expression.</summary>
    string Latex,

    /// <summary>Human-readable explanation of the generated expression.</summary>
    string Explanation,

    /// <summary>Confidence score between 0 and 1.</summary>
    double Confidence
);

/// <summary>
/// Result of fixing a broken LaTeX expression.
/// </summary>
public record MathFixResult(
    /// <summary>The corrected LaTeX expression.</summary>
    string FixedLatex,

    /// <summary>List of changes that were applied.</summary>
    List<string> Changes,

    /// <summary>Confidence score between 0 and 1.</summary>
    double Confidence
);

/// <summary>
/// Result of improving a piece of writing.
/// </summary>
public record WritingResult(
    /// <summary>The improved text.</summary>
    string ImprovedText,

    /// <summary>Summary of changes made.</summary>
    List<string> Changes,

    /// <summary>The action that was applied (improve, paraphrase, expand, shorten, formalize).</summary>
    string Action
);

/// <summary>
/// Result of classifying raw text into a block type.
/// </summary>
public record BlockClassificationResult(
    /// <summary>The suggested block type.</summary>
    string SuggestedType,

    /// <summary>Confidence score between 0 and 1.</summary>
    double Confidence,

    /// <summary>Human-readable reasoning for the classification.</summary>
    string Reasoning
);
