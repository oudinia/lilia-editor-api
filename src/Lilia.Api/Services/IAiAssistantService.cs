using Lilia.Core.Models.AiAssistant;

namespace Lilia.Api.Services;

public interface IAiAssistantService
{
    /// <summary>
    /// Generate LaTeX from a natural language description.
    /// </summary>
    Task<MathGenerationResult> GenerateMathAsync(string description, string? context);

    /// <summary>
    /// Fix a broken LaTeX expression.
    /// </summary>
    Task<MathFixResult> FixMathAsync(string brokenLatex, string? errorMessage);

    /// <summary>
    /// Improve academic writing with the specified action.
    /// </summary>
    Task<WritingResult> ImproveWritingAsync(string text, string action, string? style);

    /// <summary>
    /// Classify raw text content into a block type.
    /// </summary>
    Task<BlockClassificationResult> ClassifyBlockAsync(string content);
}
