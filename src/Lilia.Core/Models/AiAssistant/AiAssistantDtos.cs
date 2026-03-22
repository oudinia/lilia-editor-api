namespace Lilia.Core.Models.AiAssistant;

// -----------------------------------------------------------------------
// Request DTOs
// -----------------------------------------------------------------------

/// <summary>
/// Request to generate LaTeX from a natural language description.
/// </summary>
public record MathGenerateRequest(
    /// <summary>Natural language description of the math expression (e.g. "integral of x squared from 0 to 1").</summary>
    string Description,

    /// <summary>Optional surrounding context to help disambiguate.</summary>
    string? Context
);

/// <summary>
/// Request to fix a broken LaTeX expression.
/// </summary>
public record MathFixRequest(
    /// <summary>The broken LaTeX expression.</summary>
    string BrokenLatex,

    /// <summary>Optional error message from the LaTeX renderer.</summary>
    string? ErrorMessage
);

/// <summary>
/// Request to improve academic writing.
/// </summary>
public record WritingImproveRequest(
    /// <summary>The text to improve.</summary>
    string Text,

    /// <summary>The improvement action: improve, paraphrase, expand, shorten, formalize.</summary>
    string Action,

    /// <summary>Optional writing style hint (e.g. "academic", "technical").</summary>
    string? Style
);

/// <summary>
/// Request to classify raw text into a block type.
/// </summary>
public record BlockClassifyRequest(
    /// <summary>The raw text content to classify.</summary>
    string Content
);
