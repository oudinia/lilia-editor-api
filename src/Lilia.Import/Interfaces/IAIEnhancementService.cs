using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Service for AI-enhanced document analysis and classification.
/// </summary>
public interface IAIEnhancementService
{
    /// <summary>
    /// Classifies whether text is a heading and determines its level.
    /// </summary>
    /// <param name="text">The text content to classify.</param>
    /// <param name="formatting">Formatting information for the text.</param>
    /// <param name="context">Surrounding paragraphs for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Heading classification result, or null if AI is unavailable.</returns>
    Task<HeadingClassification?> ClassifyHeadingAsync(
        string text,
        FormattingInfo formatting,
        ParagraphContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes the overall structure of a document.
    /// </summary>
    /// <param name="elements">All elements in the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document structure analysis result.</returns>
    Task<DocumentStructure> AnalyzeStructureAsync(
        List<ImportElement> elements,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies a content block (theorem, definition, proof, code, etc.).
    /// </summary>
    /// <param name="text">The text content to classify.</param>
    /// <param name="precedingContext">Text of preceding paragraphs for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Block classification result.</returns>
    Task<BlockClassification> ClassifyContentAsync(
        string text,
        string precedingContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses unstructured reference/bibliography text into structured entries.
    /// </summary>
    /// <param name="referenceTexts">List of reference text strings to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of parsed bibliography entries.</returns>
    Task<List<BibEntry>> ParseReferencesAsync(
        List<string> referenceTexts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects inline equations written as plain text.
    /// </summary>
    /// <param name="text">Text that may contain inline equations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of detected equation spans with LaTeX conversions.</returns>
    Task<List<EquationSpan>> DetectEquationsAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs full AI enhancement on an imported document.
    /// </summary>
    /// <param name="document">The imported document to enhance.</param>
    /// <param name="options">AI enhancement options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AI enhancement result with all classifications and detections.</returns>
    Task<AIEnhancementResult> EnhanceDocumentAsync(
        ImportDocument document,
        AIEnhancementOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the AI service is available and configured.
    /// </summary>
    /// <returns>True if the service is available.</returns>
    Task<bool> IsAvailableAsync();
}

/// <summary>
/// Options for AI enhancement processing.
/// </summary>
public class AIEnhancementOptions
{
    /// <summary>
    /// Whether to perform heading classification.
    /// </summary>
    public bool ClassifyHeadings { get; set; } = true;

    /// <summary>
    /// Whether to analyze document structure.
    /// </summary>
    public bool AnalyzeStructure { get; set; } = true;

    /// <summary>
    /// Whether to classify content blocks (theorems, proofs, etc.).
    /// </summary>
    public bool ClassifyContent { get; set; } = true;

    /// <summary>
    /// Whether to parse bibliography/references.
    /// </summary>
    public bool ParseBibliography { get; set; } = true;

    /// <summary>
    /// Whether to detect inline equations.
    /// </summary>
    public bool DetectEquations { get; set; } = true;

    /// <summary>
    /// Minimum confidence threshold for AI classifications (0.0-1.0).
    /// Classifications below this threshold are discarded.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Maximum number of concurrent AI requests.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 5;

    /// <summary>
    /// Timeout for individual AI requests in milliseconds.
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Whether to use batch processing for multiple items.
    /// </summary>
    public bool UseBatchProcessing { get; set; } = true;

    /// <summary>
    /// Maximum items per batch request.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Default options for AI enhancement.
    /// </summary>
    public static AIEnhancementOptions Default => new();

    /// <summary>
    /// Minimal options - only heading classification.
    /// </summary>
    public static AIEnhancementOptions HeadingsOnly => new()
    {
        ClassifyHeadings = true,
        AnalyzeStructure = false,
        ClassifyContent = false,
        ParseBibliography = false,
        DetectEquations = false
    };

    /// <summary>
    /// Full analysis options - all features enabled.
    /// </summary>
    public static AIEnhancementOptions Full => new()
    {
        ClassifyHeadings = true,
        AnalyzeStructure = true,
        ClassifyContent = true,
        ParseBibliography = true,
        DetectEquations = true,
        ConfidenceThreshold = 0.6
    };
}
