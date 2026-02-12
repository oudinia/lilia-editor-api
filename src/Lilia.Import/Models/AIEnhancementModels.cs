namespace Lilia.Import.Models;

/// <summary>
/// Result of AI heading classification.
/// </summary>
public class HeadingClassification
{
    /// <summary>
    /// Whether the text is classified as a heading.
    /// </summary>
    public bool IsHeading { get; set; }

    /// <summary>
    /// Heading level (1-6) if classified as heading, null otherwise.
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// Confidence score (0.0-1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// AI reasoning for the classification.
    /// </summary>
    public string? Reasoning { get; set; }
}

/// <summary>
/// Result of AI document structure analysis.
/// </summary>
public class DocumentStructure
{
    /// <summary>
    /// Detected document type.
    /// </summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// Confidence score for document type detection (0.0-1.0).
    /// </summary>
    public double TypeConfidence { get; set; }

    /// <summary>
    /// Detected sections within the document.
    /// </summary>
    public List<DetectedSection> Sections { get; set; } = [];

    /// <summary>
    /// Overall confidence score for structure analysis (0.0-1.0).
    /// </summary>
    public double OverallConfidence { get; set; }
}

/// <summary>
/// Types of documents that can be detected.
/// </summary>
public enum DocumentType
{
    Unknown,
    ResearchPaper,
    Thesis,
    Report,
    Letter,
    Memo,
    Article,
    Essay,
    TechnicalManual,
    LegalDocument,
    BusinessProposal
}

/// <summary>
/// A section detected within a document.
/// </summary>
public class DetectedSection
{
    /// <summary>
    /// Section name/type (e.g., "abstract", "introduction", "methods").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Normalized section type for standardized processing.
    /// </summary>
    public SectionType Type { get; set; }

    /// <summary>
    /// Start block index (0-based).
    /// </summary>
    public int StartBlock { get; set; }

    /// <summary>
    /// End block index (exclusive).
    /// </summary>
    public int EndBlock { get; set; }

    /// <summary>
    /// Confidence score for this section detection (0.0-1.0).
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Standard section types in academic/professional documents.
/// </summary>
public enum SectionType
{
    Unknown,
    Title,
    Abstract,
    Introduction,
    Background,
    LiteratureReview,
    Methods,
    Results,
    Discussion,
    Conclusion,
    Acknowledgements,
    References,
    Appendix,
    TableOfContents,
    ListOfFigures,
    ListOfTables
}

/// <summary>
/// Result of AI content block classification.
/// </summary>
public class BlockClassification
{
    /// <summary>
    /// Classified block type.
    /// </summary>
    public ClassifiedBlockType BlockType { get; set; }

    /// <summary>
    /// Confidence score (0.0-1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Label for the block (e.g., "Theorem 1", "Definition 2.1").
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Programming language if code block was detected.
    /// </summary>
    public string? ProgrammingLanguage { get; set; }

    /// <summary>
    /// AI reasoning for the classification.
    /// </summary>
    public string? Reasoning { get; set; }
}

/// <summary>
/// Types of content blocks that can be classified by AI.
/// </summary>
public enum ClassifiedBlockType
{
    Paragraph,
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
    CodeBlock,
    Quote,
    Algorithm,
    Exercise,
    Solution
}

/// <summary>
/// A parsed bibliography/reference entry.
/// </summary>
public class BibEntry
{
    /// <summary>
    /// Type of bibliography entry.
    /// </summary>
    public BibEntryType Type { get; set; }

    /// <summary>
    /// List of authors.
    /// </summary>
    public List<BibAuthor> Authors { get; set; } = [];

    /// <summary>
    /// Title of the work.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Year of publication.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Journal or magazine name.
    /// </summary>
    public string? Journal { get; set; }

    /// <summary>
    /// Book title (for chapters or proceedings).
    /// </summary>
    public string? BookTitle { get; set; }

    /// <summary>
    /// Publisher name.
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Volume number.
    /// </summary>
    public string? Volume { get; set; }

    /// <summary>
    /// Issue number.
    /// </summary>
    public string? Issue { get; set; }

    /// <summary>
    /// Page numbers.
    /// </summary>
    public string? Pages { get; set; }

    /// <summary>
    /// Digital Object Identifier.
    /// </summary>
    public string? DOI { get; set; }

    /// <summary>
    /// URL of the resource.
    /// </summary>
    public string? URL { get; set; }

    /// <summary>
    /// ISBN for books.
    /// </summary>
    public string? ISBN { get; set; }

    /// <summary>
    /// Conference name (for proceedings).
    /// </summary>
    public string? Conference { get; set; }

    /// <summary>
    /// Institution (for theses and technical reports).
    /// </summary>
    public string? Institution { get; set; }

    /// <summary>
    /// Edition of the book.
    /// </summary>
    public string? Edition { get; set; }

    /// <summary>
    /// Original citation number/label from the document.
    /// </summary>
    public string? OriginalLabel { get; set; }

    /// <summary>
    /// Original raw text of the citation.
    /// </summary>
    public string? OriginalText { get; set; }

    /// <summary>
    /// Confidence score for the parsing (0.0-1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Detected citation style.
    /// </summary>
    public CitationStyle DetectedStyle { get; set; }
}

/// <summary>
/// Author information for bibliography entries.
/// </summary>
public class BibAuthor
{
    /// <summary>
    /// First/given name(s).
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Last/family name.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Middle name or initial.
    /// </summary>
    public string? MiddleName { get; set; }

    /// <summary>
    /// Suffix (Jr., III, etc.).
    /// </summary>
    public string? Suffix { get; set; }

    /// <summary>
    /// Full name as originally formatted.
    /// </summary>
    public string? FullName { get; set; }
}

/// <summary>
/// Types of bibliography entries.
/// </summary>
public enum BibEntryType
{
    Unknown,
    Article,
    Book,
    InBook,
    InCollection,
    InProceedings,
    Proceedings,
    MastersThesis,
    PhdThesis,
    TechReport,
    Unpublished,
    Misc,
    Online,
    Manual,
    Patent
}

/// <summary>
/// Citation styles that can be detected.
/// </summary>
public enum CitationStyle
{
    Unknown,
    APA,
    MLA,
    Chicago,
    IEEE,
    Harvard,
    Vancouver,
    AMA,
    Turabian,
    CSE,
    ACS
}

/// <summary>
/// A detected equation span within text.
/// </summary>
public class EquationSpan
{
    /// <summary>
    /// Start position in the original text.
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Length of the equation span in the original text.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Original text representation.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Converted LaTeX representation.
    /// </summary>
    public string LaTeX { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a display equation (vs inline).
    /// </summary>
    public bool IsDisplay { get; set; }

    /// <summary>
    /// Confidence score (0.0-1.0).
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Formatting information sent to AI for classification.
/// </summary>
public class FormattingInfo
{
    /// <summary>
    /// Whether the text is bold.
    /// </summary>
    public bool IsBold { get; set; }

    /// <summary>
    /// Whether the text is italic.
    /// </summary>
    public bool IsItalic { get; set; }

    /// <summary>
    /// Whether the text is underlined.
    /// </summary>
    public bool IsUnderline { get; set; }

    /// <summary>
    /// Font size in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Font family name.
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Whether font is monospace.
    /// </summary>
    public bool IsMonospace { get; set; }

    /// <summary>
    /// Whether text is all uppercase.
    /// </summary>
    public bool IsAllCaps { get; set; }

    /// <summary>
    /// Paragraph style name from the document.
    /// </summary>
    public string? StyleName { get; set; }

    /// <summary>
    /// Indentation level.
    /// </summary>
    public int IndentLevel { get; set; }

    /// <summary>
    /// Whether paragraph has a numbered prefix.
    /// </summary>
    public bool HasNumberedPrefix { get; set; }

    /// <summary>
    /// Background/highlight color if any.
    /// </summary>
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// Creates FormattingInfo from a list of formatting spans.
    /// </summary>
    public static FormattingInfo FromSpans(string text, List<FormattingSpan>? spans)
    {
        var info = new FormattingInfo
        {
            IsAllCaps = !string.IsNullOrEmpty(text) && text == text.ToUpperInvariant() && text.Any(char.IsLetter)
        };

        if (spans == null || spans.Count == 0)
            return info;

        // Check if majority of text has certain formatting
        var textLength = text.Length;
        var boldLength = spans.Where(s => s.Type == FormattingType.Bold).Sum(s => s.Length);
        var italicLength = spans.Where(s => s.Type == FormattingType.Italic).Sum(s => s.Length);
        var underlineLength = spans.Where(s => s.Type == FormattingType.Underline).Sum(s => s.Length);

        info.IsBold = boldLength > textLength * 0.5;
        info.IsItalic = italicLength > textLength * 0.5;
        info.IsUnderline = underlineLength > textLength * 0.5;

        // Get font size from first span that has it
        var fontSizeSpan = spans.FirstOrDefault(s => s.Type == FormattingType.FontSize && s.Value != null);
        if (fontSizeSpan != null && double.TryParse(fontSizeSpan.Value, out var fontSize))
        {
            info.FontSize = fontSize;
        }

        // Get font family
        var fontFamilySpan = spans.FirstOrDefault(s => s.Type == FormattingType.FontFamily && s.Value != null);
        if (fontFamilySpan != null)
        {
            info.FontFamily = fontFamilySpan.Value;
            info.IsMonospace = IsMonospaceFont(fontFamilySpan.Value!);
        }

        // Get highlight
        var highlightSpan = spans.FirstOrDefault(s => s.Type == FormattingType.Highlight && s.Value != null);
        if (highlightSpan != null)
        {
            info.BackgroundColor = highlightSpan.Value;
        }

        return info;
    }

    private static bool IsMonospaceFont(string fontName)
    {
        return Detection.MonospaceFontList.Default.Contains(fontName);
    }
}

/// <summary>
/// Context for AI analysis - surrounding paragraphs.
/// </summary>
public class ParagraphContext
{
    /// <summary>
    /// Paragraphs before the target (in order, closest last).
    /// </summary>
    public List<ContextParagraph> Before { get; set; } = [];

    /// <summary>
    /// Paragraphs after the target (in order, closest first).
    /// </summary>
    public List<ContextParagraph> After { get; set; } = [];
}

/// <summary>
/// A paragraph in the context.
/// </summary>
public class ContextParagraph
{
    /// <summary>
    /// Text content (truncated if too long).
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Element type (heading, paragraph, etc.).
    /// </summary>
    public ImportElementType ElementType { get; set; }

    /// <summary>
    /// Heading level if it's a heading.
    /// </summary>
    public int? HeadingLevel { get; set; }
}

/// <summary>
/// Result of AI enhancement processing.
/// </summary>
public class AIEnhancementResult
{
    /// <summary>
    /// Whether the enhancement was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if enhancement failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Document structure analysis result.
    /// </summary>
    public DocumentStructure? Structure { get; set; }

    /// <summary>
    /// Heading classifications (keyed by element order).
    /// </summary>
    public Dictionary<int, HeadingClassification> HeadingClassifications { get; set; } = [];

    /// <summary>
    /// Block classifications (keyed by element order).
    /// </summary>
    public Dictionary<int, BlockClassification> BlockClassifications { get; set; } = [];

    /// <summary>
    /// Parsed bibliography entries.
    /// </summary>
    public List<BibEntry> BibliographyEntries { get; set; } = [];

    /// <summary>
    /// Detected equations (keyed by element order, list of spans).
    /// </summary>
    public Dictionary<int, List<EquationSpan>> DetectedEquations { get; set; } = [];

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Total tokens used in AI requests.
    /// </summary>
    public int TotalTokensUsed { get; set; }
}
