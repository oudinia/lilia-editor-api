namespace Lilia.Import.Models;

/// <summary>
/// Records what happened to a single body element during import.
/// Every paragraph, table, and other element from the DOCX body is traced,
/// making it trivial to diagnose what content was imported, skipped, or misclassified.
/// </summary>
public class ParagraphTraceEntry
{
    /// <summary>
    /// Zero-based index of this element in the document body.
    /// </summary>
    public int BodyIndex { get; set; }

    /// <summary>
    /// The OpenXml element type (e.g., "Paragraph", "Table", "SdtBlock").
    /// </summary>
    public string ElementType { get; set; } = string.Empty;

    /// <summary>
    /// Raw text content (first 500 chars) extracted from the element.
    /// </summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Full raw text content (no truncation).
    /// </summary>
    public string FullText { get; set; } = string.Empty;

    /// <summary>
    /// The paragraph style ID from OpenXml (e.g., "Heading1", "Normal", "Title").
    /// Null for non-paragraph elements.
    /// </summary>
    public string? StyleId { get; set; }

    /// <summary>
    /// Font family of the first run (e.g., "Consolas", "Calibri").
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Font size in points of the first run.
    /// </summary>
    public double? FontSizePoints { get; set; }

    /// <summary>
    /// Whether all runs are bold.
    /// </summary>
    public bool AllBold { get; set; }

    /// <summary>
    /// Whether all runs are italic.
    /// </summary>
    public bool AllItalic { get; set; }

    /// <summary>
    /// Whether the paragraph has numbering (list) properties.
    /// </summary>
    public bool HasNumbering { get; set; }

    /// <summary>
    /// Whether the paragraph has math elements.
    /// </summary>
    public bool HasMathElements { get; set; }

    /// <summary>
    /// Whether the paragraph has drawing elements (images).
    /// </summary>
    public bool HasDrawings { get; set; }

    /// <summary>
    /// Whether the paragraph has page breaks.
    /// </summary>
    public bool HasPageBreaks { get; set; }

    /// <summary>
    /// Shading fill color (hex, e.g., "E0E0E0").
    /// </summary>
    public string? ShadingFill { get; set; }

    /// <summary>
    /// Outline level from paragraph properties.
    /// </summary>
    public int? OutlineLevel { get; set; }

    /// <summary>
    /// Left indent in twips.
    /// </summary>
    public int IndentLeftTwips { get; set; }

    /// <summary>
    /// Whether the paragraph has a left border.
    /// </summary>
    public bool HasLeftBorder { get; set; }

    /// <summary>
    /// The ID of the detection rule that matched (e.g., "heading.style", "code.font").
    /// "none" if no rule matched. "n/a" for non-paragraph elements.
    /// </summary>
    public string MatchedRuleId { get; set; } = "none";

    /// <summary>
    /// The name of the matched rule for display.
    /// </summary>
    public string? MatchedRuleName { get; set; }

    /// <summary>
    /// The detected import element type (e.g., "Heading", "Paragraph", "CodeBlock").
    /// "Dropped" if the element was not included. "Table" or "SdtBlock" for non-paragraph elements.
    /// </summary>
    public string DetectedType { get; set; } = "Unknown";

    /// <summary>
    /// Number of import elements produced by this body element.
    /// 0 means the element was consumed or dropped.
    /// </summary>
    public int ElementsProduced { get; set; }

    /// <summary>
    /// The section tracker state at the time of evaluation.
    /// </summary>
    public string? CurrentSection { get; set; }

    /// <summary>
    /// Whether InAbstractSection was true at the time of evaluation.
    /// </summary>
    public bool InAbstractSection { get; set; }

    /// <summary>
    /// Additional notes about what happened (e.g., "Consumed as abstract heading marker").
    /// </summary>
    public string? Notes { get; set; }
}
