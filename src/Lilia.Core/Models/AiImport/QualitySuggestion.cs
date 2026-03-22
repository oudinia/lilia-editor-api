namespace Lilia.Core.Models.AiImport;

/// <summary>
/// An AI-generated quality improvement suggestion for a block.
/// </summary>
public record QualitySuggestion(
    /// <summary>Category of the suggestion: formatting, structure, content, or math.</summary>
    string Category,

    /// <summary>Human-readable description of the issue.</summary>
    string Description,

    /// <summary>Suggested fix or improvement (may contain replacement text).</summary>
    string? SuggestedFix,

    /// <summary>Severity level: info, warning, or error.</summary>
    string Severity
)
{
    public static class Categories
    {
        public const string Formatting = "formatting";
        public const string Structure = "structure";
        public const string Content = "content";
        public const string Math = "math";
    }

    public static class Severities
    {
        public const string Info = "info";
        public const string Warning = "warning";
        public const string Error = "error";
    }
}
