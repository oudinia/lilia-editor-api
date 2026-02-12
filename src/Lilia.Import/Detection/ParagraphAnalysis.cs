using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lilia.Import.Models;
using M = DocumentFormat.OpenXml.Math;

namespace Lilia.Import.Detection;

/// <summary>
/// Pre-computed analysis of an OpenXml Paragraph, extracted once and used by all detection rules.
/// Avoids repeated tree traversal of the same paragraph.
/// </summary>
public class ParagraphAnalysis
{
    /// <summary>
    /// The paragraph style ID (e.g., "Heading1", "Normal", "Title").
    /// </summary>
    public string? StyleId { get; init; }

    /// <summary>
    /// Plain text content extracted from the paragraph.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Formatting spans extracted from the paragraph.
    /// </summary>
    public List<FormattingSpan> Formatting { get; init; } = [];

    /// <summary>
    /// Font family of the first run or paragraph mark.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>
    /// Font size in points of the first run (null if not specified).
    /// </summary>
    public double? FontSizePoints { get; init; }

    /// <summary>
    /// Whether all runs in the paragraph are bold.
    /// </summary>
    public bool AllBold { get; init; }

    /// <summary>
    /// Whether all runs in the paragraph are italic.
    /// </summary>
    public bool AllItalic { get; init; }

    /// <summary>
    /// Whether the text is all uppercase letters.
    /// </summary>
    public bool AllCaps { get; init; }

    /// <summary>
    /// Whether the paragraph has numbering properties.
    /// </summary>
    public bool HasNumberingProperties { get; init; }

    /// <summary>
    /// The NumberingProperties element (null if not a list item).
    /// </summary>
    public NumberingProperties? NumberingProperties { get; init; }

    /// <summary>
    /// Whether this is a numbered list (vs bullet).
    /// Only meaningful when HasNumberingProperties is true.
    /// </summary>
    public bool IsNumberedList { get; set; }

    /// <summary>
    /// Numbering nesting level (0-based).
    /// </summary>
    public int NumberingLevel { get; init; }

    /// <summary>
    /// Numbering ID for list association.
    /// </summary>
    public int? NumberingId { get; init; }

    /// <summary>
    /// Whether the paragraph contains OMML math elements.
    /// </summary>
    public bool HasMathElements { get; init; }

    /// <summary>
    /// List of OMML math elements found in the paragraph.
    /// </summary>
    public List<M.OfficeMath> MathElements { get; init; } = [];

    /// <summary>
    /// Whether the paragraph contains Drawing elements (images).
    /// </summary>
    public bool HasDrawings { get; init; }

    /// <summary>
    /// Whether the paragraph contains page break elements.
    /// </summary>
    public bool HasPageBreaks { get; init; }

    /// <summary>
    /// Shading fill color (hex string like "E0E0E0") from paragraph properties.
    /// </summary>
    public string? ShadingFill { get; init; }

    /// <summary>
    /// Outline level from paragraph properties (0-based, null if not set).
    /// </summary>
    public int? OutlineLevel { get; init; }

    /// <summary>
    /// Left indent in twips from paragraph properties.
    /// </summary>
    public int IndentLeftTwips { get; init; }

    /// <summary>
    /// Whether the paragraph has a left border.
    /// </summary>
    public bool HasLeftBorder { get; init; }

    /// <summary>
    /// Current document section type (set by pipeline from SectionTracker).
    /// </summary>
    public Models.SectionType CurrentSection { get; set; } = Models.SectionType.Unknown;

    /// <summary>
    /// Whether we're currently inside an abstract section (set by pipeline from SectionTracker).
    /// </summary>
    public bool InAbstractSection { get; set; }

    /// <summary>
    /// The raw OpenXml Paragraph element (for advanced rules needing direct access).
    /// </summary>
    public Paragraph RawParagraph { get; init; } = null!;

    /// <summary>
    /// The MainDocumentPart (for rules needing numbering definitions, etc.).
    /// </summary>
    public MainDocumentPart MainDocumentPart { get; init; } = null!;

    /// <summary>
    /// All runs in the paragraph (pre-extracted).
    /// </summary>
    public List<Run> Runs { get; init; } = [];
}
