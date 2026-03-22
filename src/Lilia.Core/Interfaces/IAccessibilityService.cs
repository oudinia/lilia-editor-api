using Lilia.Core.Models.MathAst;

namespace Lilia.Core.Interfaces;

/// <summary>
/// Service for generating accessible representations of document content,
/// including MathML output, natural-language narration, and block validation.
/// </summary>
public interface IAccessibilityService
{
    /// <summary>
    /// Generate MathML Presentation markup for the given math AST node.
    /// </summary>
    string GenerateMathML(MathNode node);

    /// <summary>
    /// Generate natural-language narration for the given math AST node.
    /// </summary>
    string NarrateMath(MathNode node);

    /// <summary>
    /// Validate a block for accessibility issues (missing alt text, labels, empty content, etc.).
    /// </summary>
    List<AccessibilityWarning> ValidateBlock(string blockType, string contentJson);
}

/// <summary>
/// Represents an accessibility issue found during block validation.
/// </summary>
public class AccessibilityWarning
{
    /// <summary>Severity level: "error", "warning", or "info".</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Human-readable description of the issue.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The field or property that has the issue.</summary>
    public string Field { get; set; } = string.Empty;
}
