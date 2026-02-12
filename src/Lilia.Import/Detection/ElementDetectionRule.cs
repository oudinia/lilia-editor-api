using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Import.Detection;

/// <summary>
/// A single detection rule that maps a condition to an element type.
/// Rules are evaluated in priority order; the first match wins.
/// </summary>
public class ElementDetectionRule
{
    /// <summary>
    /// Unique identifier for this rule (e.g., "heading.style", "code.font").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name for this rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Priority determines evaluation order. Lower values are evaluated first.
    /// Priority bands:
    ///   0-99:   Page breaks
    ///   100-199: Headings
    ///   200-299: Title/Subtitle abstract reclassification
    ///   300-399: Lists
    ///   400-499: Equations
    ///   500-599: Section-context types (abstract, bibliography)
    ///   600-699: Semantic types (theorem, blockquote)
    ///   700-799: Code blocks
    ///   800-899: Images
    ///   900-999: Fallback paragraph
    /// </summary>
    public required int Priority { get; init; }

    /// <summary>
    /// The target element type this rule detects.
    /// </summary>
    public required ImportElementType TargetType { get; init; }

    /// <summary>
    /// The condition that must be true for this rule to match.
    /// </summary>
    public required DetectionCondition Condition { get; init; }

    /// <summary>
    /// Whether this rule is enabled. Disabled rules are skipped during evaluation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Creates the import elements when this rule matches.
    /// Parameters: (ParagraphAnalysis analysis, DocxParser parser)
    /// Returns null or empty to indicate the paragraph should be consumed without producing elements.
    /// </summary>
    public required Func<ParagraphAnalysis, DocxParser, IEnumerable<ImportElement>?> CreateElements { get; init; }

    /// <summary>
    /// Optional side-effect when this rule matches (e.g., updating the section tracker).
    /// Called after CreateElements.
    /// </summary>
    public Action<ParagraphAnalysis, SectionTracker>? OnMatch { get; init; }
}
