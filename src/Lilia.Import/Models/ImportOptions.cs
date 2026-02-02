namespace Lilia.Import.Models;

/// <summary>
/// Options for configuring the DOCX import process.
/// </summary>
public class ImportOptions
{
    /// <summary>
    /// Maximum number of sections to import (0 = unlimited).
    /// Express tier: 5 sections
    /// Paid tiers: unlimited
    /// Default: 0 (unlimited)
    /// </summary>
    public int MaxSections { get; set; } = 0;

    /// <summary>
    /// Maximum number of blocks/paragraphs to import (0 = unlimited).
    /// Used as a fallback if document has no sections.
    /// Default: 0 (unlimited)
    /// </summary>
    public int MaxBlocks { get; set; } = 0;

    /// <summary>
    /// Whether the import was truncated due to limits.
    /// Set by the import process.
    /// </summary>
    public bool WasTruncated { get; set; } = false;

    /// <summary>
    /// Whether to detect code blocks by style name (e.g., "Code", "Preformatted").
    /// Default: true
    /// </summary>
    public bool DetectCodeByStyle { get; set; } = true;

    /// <summary>
    /// Whether to detect code blocks by monospace font family.
    /// Default: true
    /// </summary>
    public bool DetectCodeByFont { get; set; } = true;

    /// <summary>
    /// Whether to detect code blocks by background shading.
    /// Default: true
    /// </summary>
    public bool DetectCodeByShading { get; set; } = true;

    /// <summary>
    /// Minimum heading level to treat as section (1-9).
    /// Headings below this level will be treated as bold paragraphs.
    /// Default: 1 (all headings become sections)
    /// </summary>
    public int MinHeadingLevelForSection { get; set; } = 1;

    /// <summary>
    /// Maximum heading level to treat as section (1-9).
    /// Headings above this level will be treated as bold paragraphs.
    /// Default: 6 (H1-H6 become sections, H7-H9 become paragraphs)
    /// </summary>
    public int MaxHeadingLevelForSection { get; set; } = 6;

    /// <summary>
    /// Whether to attempt OMML to LaTeX conversion.
    /// Default: true
    /// </summary>
    public bool ConvertEquationsToLatex { get; set; } = true;

    /// <summary>
    /// Whether to preserve text formatting (bold, italic, etc.).
    /// Default: true
    /// </summary>
    public bool PreserveFormatting { get; set; } = true;

    /// <summary>
    /// Whether to extract images from the document.
    /// Default: true
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Whether to extract tables from the document.
    /// Default: true
    /// </summary>
    public bool ExtractTables { get; set; } = true;

    /// <summary>
    /// List of font families to consider as monospace (for code detection).
    /// </summary>
    public HashSet<string> MonospaceFonts { get; set; } =
    [
        "Consolas",
        "Courier New",
        "Courier",
        "Monaco",
        "Menlo",
        "Lucida Console",
        "Liberation Mono",
        "DejaVu Sans Mono",
        "Source Code Pro",
        "Fira Code",
        "JetBrains Mono",
        "Cascadia Code",
        "Cascadia Mono"
    ];

    /// <summary>
    /// List of style names that indicate code blocks (case-insensitive partial match).
    /// </summary>
    public HashSet<string> CodeStylePatterns { get; set; } =
    [
        "Code",
        "Preformatted",
        "SourceCode",
        "Listing",
        "Verbatim",
        "Monospace"
    ];

    /// <summary>
    /// Whether to merge consecutive paragraphs that appear to be the same logical paragraph
    /// (e.g., soft line breaks in Word).
    /// Default: false
    /// </summary>
    public bool MergeConsecutiveParagraphs { get; set; } = false;

    /// <summary>
    /// Whether to detect headings using formatting heuristics when standard heading styles
    /// are not used. This detects:
    /// - Numbered sections (e.g., "1. Introduction", "1.1 Methods")
    /// - Roman numeral sections (e.g., "I. Introduction")
    /// - Bold text with larger font sizes
    /// - ALL CAPS short text
    /// Default: true
    /// </summary>
    public bool DetectHeadingsByFormatting { get; set; } = true;

    /// <summary>
    /// Image optimization settings.
    /// Default: Balanced (1920px max, 85% JPEG quality)
    /// </summary>
    public ImageImportOptions ImageOptions { get; set; } = new();

    /// <summary>
    /// AI enhancement options.
    /// Default: Disabled (null)
    /// </summary>
    public AIImportOptions? AIOptions { get; set; }

    /// <summary>
    /// Whether AI enhancement is enabled.
    /// Shorthand for AIOptions != null.
    /// </summary>
    public bool UseAIEnhancement => AIOptions?.Enabled == true;

    /// <summary>
    /// Default import options.
    /// </summary>
    public static ImportOptions Default => new();

    /// <summary>
    /// Import options with AI enhancement enabled.
    /// </summary>
    public static ImportOptions WithAIEnhancement => new()
    {
        AIOptions = AIImportOptions.Default
    };
}

/// <summary>
/// Options for AI-enhanced import processing.
/// </summary>
public class AIImportOptions
{
    /// <summary>
    /// Whether AI enhancement is enabled.
    /// Default: true (when AIImportOptions is used)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use AI for heading detection when heuristics are uncertain.
    /// Default: true
    /// </summary>
    public bool HeadingDetection { get; set; } = true;

    /// <summary>
    /// Whether to use AI for document structure analysis.
    /// Identifies document type and major sections.
    /// Default: true
    /// </summary>
    public bool StructureAnalysis { get; set; } = true;

    /// <summary>
    /// Whether to use AI for content classification.
    /// Detects theorems, proofs, definitions, code blocks, etc.
    /// Default: true
    /// </summary>
    public bool ContentClassification { get; set; } = true;

    /// <summary>
    /// Whether to use AI for bibliography/citation parsing.
    /// Extracts structured citation data from reference lists.
    /// Default: true
    /// </summary>
    public bool BibliographyParsing { get; set; } = true;

    /// <summary>
    /// Whether to use AI for inline equation detection.
    /// Finds equations written as plain text and converts to LaTeX.
    /// Default: true
    /// </summary>
    public bool EquationDetection { get; set; } = true;

    /// <summary>
    /// Minimum confidence threshold for AI classifications (0.0-1.0).
    /// Classifications below this threshold fall back to heuristic methods.
    /// Default: 0.7
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Maximum time to wait for AI processing in milliseconds.
    /// Default: 60000 (1 minute)
    /// </summary>
    public int TimeoutMs { get; set; } = 60000;

    /// <summary>
    /// Whether to continue with heuristic-only import if AI fails.
    /// Default: true
    /// </summary>
    public bool FallbackOnError { get; set; } = true;

    /// <summary>
    /// Default AI import options (all features enabled).
    /// </summary>
    public static AIImportOptions Default => new();

    /// <summary>
    /// Minimal AI options - only heading detection.
    /// </summary>
    public static AIImportOptions HeadingsOnly => new()
    {
        HeadingDetection = true,
        StructureAnalysis = false,
        ContentClassification = false,
        BibliographyParsing = false,
        EquationDetection = false
    };

    /// <summary>
    /// Academic paper options - optimized for research papers.
    /// </summary>
    public static AIImportOptions AcademicPaper => new()
    {
        HeadingDetection = true,
        StructureAnalysis = true,
        ContentClassification = true,
        BibliographyParsing = true,
        EquationDetection = true,
        ConfidenceThreshold = 0.6
    };

    /// <summary>
    /// Technical document options - code and equation focused.
    /// </summary>
    public static AIImportOptions TechnicalDocument => new()
    {
        HeadingDetection = true,
        StructureAnalysis = false,
        ContentClassification = true,
        BibliographyParsing = false,
        EquationDetection = true,
        ConfidenceThreshold = 0.75
    };
}

/// <summary>
/// Options for image handling during import.
/// </summary>
public class ImageImportOptions
{
    /// <summary>
    /// Whether to optimize images during import.
    /// Default: true
    /// </summary>
    public bool EnableOptimization { get; set; } = true;

    /// <summary>
    /// Maximum image dimension (width or height) in pixels.
    /// Images larger than this are resized proportionally.
    /// Set to 0 to keep original size.
    /// Default: 1920 (Full HD)
    /// </summary>
    public int MaxDimension { get; set; } = 1920;

    /// <summary>
    /// JPEG compression quality (0-100).
    /// Higher values = better quality but larger files.
    /// Default: 85
    /// </summary>
    public int JpegQuality { get; set; } = 85;

    /// <summary>
    /// Minimum file size in bytes before optimization is applied.
    /// Images smaller than this are kept as-is.
    /// Default: 102400 (100 KB)
    /// </summary>
    public int OptimizeThresholdBytes { get; set; } = 100 * 1024;

    /// <summary>
    /// Whether to convert PNG photos to JPEG for better compression.
    /// Screenshots and graphics with transparency are kept as PNG.
    /// Default: true
    /// </summary>
    public bool ConvertPhotosToJpeg { get; set; } = true;

    /// <summary>
    /// Preset: Keep original images without any optimization.
    /// </summary>
    public static ImageImportOptions Original => new()
    {
        EnableOptimization = false
    };

    /// <summary>
    /// Preset: High quality (2560px max, 92% quality).
    /// Best for print-quality documents.
    /// </summary>
    public static ImageImportOptions HighQuality => new()
    {
        MaxDimension = 2560,
        JpegQuality = 92,
        OptimizeThresholdBytes = 200 * 1024
    };

    /// <summary>
    /// Preset: Balanced quality and size (1920px, 85% quality).
    /// Good for screen viewing and moderate print quality.
    /// </summary>
    public static ImageImportOptions Balanced => new()
    {
        MaxDimension = 1920,
        JpegQuality = 85,
        OptimizeThresholdBytes = 100 * 1024
    };

    /// <summary>
    /// Preset: Compact (1280px, 75% quality).
    /// Good for web/email sharing.
    /// </summary>
    public static ImageImportOptions Compact => new()
    {
        MaxDimension = 1280,
        JpegQuality = 75,
        OptimizeThresholdBytes = 50 * 1024
    };

    /// <summary>
    /// Preset: Minimal size (800px, 65% quality).
    /// For documents where images are secondary.
    /// </summary>
    public static ImageImportOptions Minimal => new()
    {
        MaxDimension = 800,
        JpegQuality = 65,
        OptimizeThresholdBytes = 20 * 1024
    };
}
