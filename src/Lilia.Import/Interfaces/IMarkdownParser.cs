using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for parsing Markdown files into the intermediate ImportDocument model.
/// This is Stage 1 of the two-stage import process.
/// </summary>
public interface IMarkdownParser
{
    /// <summary>
    /// Parse a Markdown file and extract content into the intermediate model.
    /// </summary>
    /// <param name="filePath">Path to the Markdown file.</param>
    /// <param name="options">Import options.</param>
    /// <returns>The parsed ImportDocument containing all extracted elements.</returns>
    Task<ImportDocument> ParseAsync(string filePath, MarkdownImportOptions? options = null);

    /// <summary>
    /// Check if a file can be parsed (by extension).
    /// </summary>
    /// <param name="filePath">Path to check.</param>
    /// <returns>True if the file appears to be a Markdown file.</returns>
    bool CanParse(string filePath);
}

/// <summary>
/// Options for configuring Markdown import.
/// </summary>
public class MarkdownImportOptions
{
    /// <summary>
    /// Whether to treat the first H1 as the document title.
    /// Default: true
    /// </summary>
    public bool FirstH1AsTitle { get; set; } = true;

    /// <summary>
    /// Whether to convert inline math ($...$) to inline equations.
    /// Default: true
    /// </summary>
    public bool ConvertInlineMath { get; set; } = true;

    /// <summary>
    /// Whether to convert display math ($$...$$) to equation blocks.
    /// Default: true
    /// </summary>
    public bool ConvertDisplayMath { get; set; } = true;

    /// <summary>
    /// Whether to detect and convert fenced code blocks.
    /// Default: true
    /// </summary>
    public bool ConvertCodeBlocks { get; set; } = true;

    /// <summary>
    /// Whether to preserve line breaks within paragraphs.
    /// Default: false (single line breaks are collapsed)
    /// </summary>
    public bool PreserveLineBreaks { get; set; } = false;

    /// <summary>
    /// Whether to convert images (![](...)) to figure elements.
    /// Default: true
    /// </summary>
    public bool ConvertImages { get; set; } = true;

    /// <summary>
    /// Whether to convert tables to table elements.
    /// Default: true
    /// </summary>
    public bool ConvertTables { get; set; } = true;

    /// <summary>
    /// Minimum heading level to treat as section (1-6).
    /// Default: 1 (all headings become sections)
    /// </summary>
    public int MinHeadingLevelForSection { get; set; } = 1;

    /// <summary>
    /// Maximum heading level to treat as section (1-6).
    /// Default: 6 (all headings become sections)
    /// </summary>
    public int MaxHeadingLevelForSection { get; set; } = 6;

    /// <summary>
    /// Default options.
    /// </summary>
    public static MarkdownImportOptions Default => new();
}
