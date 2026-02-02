namespace Lilia.Import.Models;

/// <summary>
/// Type of import element extracted from DOCX.
/// </summary>
public enum ImportElementType
{
    Heading,
    Paragraph,
    Equation,
    CodeBlock,
    Table,
    Image,
    ListItem,
    Header,
    Footer,
    Footnote,
    Endnote,
    TableOfContents,
    Comment,
    TrackChange,
    PageBreak
}

/// <summary>
/// How a code block was detected in the DOCX.
/// </summary>
public enum CodeBlockDetectionReason
{
    StyleName,      // Paragraph style contains "Code" or "Preformatted"
    MonospaceFont,  // Uses Consolas, Courier New, Monaco, Menlo, etc.
    Shading,        // Has gray/colored background shading
    Manual          // User manually marked as code during review
}

/// <summary>
/// Type of text formatting applied to a span.
/// </summary>
public enum FormattingType
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    Superscript,
    Subscript,
    Highlight,
    FontColor,
    FontSize,
    FontFamily
}

/// <summary>
/// Style of paragraph (beyond normal text).
/// </summary>
public enum ParagraphStyle
{
    Normal,
    Quote,
    ListItemBullet,
    ListItemNumbered,
    Title,
    Subtitle,
    Caption
}

/// <summary>
/// Type of warning generated during import.
/// </summary>
public enum ImportWarningType
{
    UnknownStyle,           // Style ID not found in document
    EquationConversionFailed,  // OMML to LaTeX conversion failed
    ImageExtractionFailed,  // Could not extract image data
    UnsupportedElement,     // Element type not supported
    FormattingLost,         // Some formatting could not be preserved
    NestedTableSkipped,     // Nested tables not supported
    MergedCellsSimplified,  // Merged table cells were simplified
    ContentTruncated        // Content was limited due to Express tier
}
