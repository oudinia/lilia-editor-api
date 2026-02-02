using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for parsing LaTeX files into the intermediate ImportDocument model.
/// This is Stage 1 of the two-stage import process.
/// </summary>
public interface ILatexParser
{
    /// <summary>
    /// Parse a LaTeX file and extract content into the intermediate model.
    /// </summary>
    /// <param name="filePath">Path to the LaTeX file.</param>
    /// <param name="options">Import options.</param>
    /// <returns>The parsed ImportDocument containing all extracted elements.</returns>
    Task<ImportDocument> ParseAsync(string filePath, LatexImportOptions? options = null);

    /// <summary>
    /// Check if a file can be parsed (by extension).
    /// </summary>
    /// <param name="filePath">Path to check.</param>
    /// <returns>True if the file appears to be a LaTeX file.</returns>
    bool CanParse(string filePath);
}

/// <summary>
/// Options for configuring LaTeX import.
/// </summary>
public class LatexImportOptions
{
    /// <summary>
    /// Whether to extract \title{} as document title.
    /// Default: true
    /// </summary>
    public bool ExtractDocumentTitle { get; set; } = true;

    /// <summary>
    /// Whether to only process content between \begin{document} and \end{document}.
    /// Default: true
    /// </summary>
    public bool OnlyDocumentContent { get; set; } = true;

    /// <summary>
    /// Whether to convert equation environments (equation, align, etc.) to equation blocks.
    /// Default: true
    /// </summary>
    public bool ConvertEquationEnvironments { get; set; } = true;

    /// <summary>
    /// Whether to convert display math ($$...$$ and \[...\]) to equation blocks.
    /// Default: true
    /// </summary>
    public bool ConvertDisplayMath { get; set; } = true;

    /// <summary>
    /// Whether to convert lstlisting/verbatim to code blocks.
    /// Default: true
    /// </summary>
    public bool ConvertCodeEnvironments { get; set; } = true;

    /// <summary>
    /// Whether to preserve figure environments.
    /// Default: true
    /// </summary>
    public bool PreserveFigures { get; set; } = true;

    /// <summary>
    /// Whether to convert table environments.
    /// Default: true
    /// </summary>
    public bool ConvertTables { get; set; } = true;

    /// <summary>
    /// Minimum heading level (\section=1, \subsection=2, etc.) to treat as section.
    /// Default: 1 (all sections become sections)
    /// </summary>
    public int MinHeadingLevelForSection { get; set; } = 1;

    /// <summary>
    /// Maximum heading level to treat as section.
    /// Default: 3 (\section, \subsection, \subsubsection)
    /// </summary>
    public int MaxHeadingLevelForSection { get; set; } = 3;

    /// <summary>
    /// Default options.
    /// </summary>
    public static LatexImportOptions Default => new();
}
