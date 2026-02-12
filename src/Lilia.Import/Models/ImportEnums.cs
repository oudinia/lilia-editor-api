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
    PageBreak,
    LatexPassthrough,  // Raw LaTeX that bypasses conversion (TikZ, custom packages, etc.)
    Abstract,
    Blockquote,
    Theorem,
    BibliographyEntry
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
/// How a blockquote was detected in the DOCX.
/// </summary>
public enum BlockquoteDetectionReason
{
    StyleName,      // Paragraph style contains "Quote", "Blockquote", etc.
    IndentItalic,   // Indented + all italic
    LeftBorder,     // Has left border and no numbering
    Manual          // User manually marked as blockquote during review
}

/// <summary>
/// Type of theorem-like environment.
/// </summary>
public enum TheoremEnvironmentType
{
    Theorem,
    Lemma,
    Proposition,
    Corollary,
    Conjecture,
    Definition,
    Example,
    Remark,
    Note,
    Proof,
    Algorithm,
    Exercise,
    Solution,
    Axiom,
    Assumption
}

/// <summary>
/// How a theorem environment was detected in the DOCX.
/// </summary>
public enum TheoremDetectionReason
{
    StyleName,      // Paragraph style contains theorem/lemma/etc.
    ContentPattern, // Text starts with "Theorem N.", "Definition N.", etc.
    AIClassified,   // Classified by AI enhancement
    Manual          // User manually marked during review
}

/// <summary>
/// How a bibliography entry was detected in the DOCX.
/// </summary>
public enum BibliographyDetectionReason
{
    SectionContext,   // Inside a References/Bibliography section
    NumberedPattern,  // Starts with [N] or N. pattern
    HangingIndent,    // Has hanging indent formatting
    AIClassified,     // Classified by AI enhancement
    Manual            // User manually marked during review
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
