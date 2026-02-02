using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for converting the intermediate ImportDocument to Lilia's native format.
/// This is Stage 2 of the two-stage import process.
/// </summary>
public interface IImportConverter
{
    /// <summary>
    /// Convert an ImportDocument to Lilia's Document format.
    /// </summary>
    /// <param name="importDocument">The intermediate representation from Stage 1.</param>
    /// <param name="options">Conversion options.</param>
    /// <returns>The import result containing the converted document and any warnings.</returns>
    ImportResult Convert(ImportDocument importDocument, ConversionOptions? options = null);
}

/// <summary>
/// Options for controlling the conversion process.
/// </summary>
public class ConversionOptions
{
    /// <summary>
    /// Maximum number of sections to convert (0 = unlimited).
    /// Express tier: 5 sections
    /// Default: 0 (unlimited)
    /// </summary>
    public int MaxSections { get; set; } = 0;

    /// <summary>
    /// Maximum number of blocks to convert (0 = unlimited).
    /// Used as fallback if document has few sections.
    /// Default: 0 (unlimited)
    /// </summary>
    public int MaxBlocks { get; set; } = 0;

    /// <summary>
    /// Whether to apply formatting as LaTeX commands in paragraph content.
    /// Default: true
    /// </summary>
    public bool ApplyFormattingAsLatex { get; set; } = true;

    /// <summary>
    /// Whether to store original OMML in equation metadata.
    /// Default: true
    /// </summary>
    public bool PreserveOmmlSource { get; set; } = true;

    /// <summary>
    /// How to handle equations that failed LaTeX conversion.
    /// </summary>
    public FailedEquationBehavior FailedEquationBehavior { get; set; } = FailedEquationBehavior.InsertPlaceholder;

    /// <summary>
    /// Whether to flatten headings above a certain level into paragraph sections.
    /// 0 = keep all heading levels as sections
    /// </summary>
    public int MaxSectionDepth { get; set; } = 0;

    /// <summary>
    /// Default language for code blocks when not detectable.
    /// </summary>
    public string DefaultCodeLanguage { get; set; } = "";

    /// <summary>
    /// Whether to generate hash for assets.
    /// </summary>
    public bool GenerateAssetHashes { get; set; } = true;

    /// <summary>
    /// Image optimization settings.
    /// Default: Balanced (1920px max, 85% JPEG quality)
    /// </summary>
    public ImageOptimizationOptions ImageOptimization { get; set; } = ImageOptimizationOptions.Balanced;
}

/// <summary>
/// Behavior when an equation fails LaTeX conversion.
/// </summary>
public enum FailedEquationBehavior
{
    /// <summary>
    /// Insert a placeholder comment with error message.
    /// </summary>
    InsertPlaceholder,

    /// <summary>
    /// Skip the equation entirely.
    /// </summary>
    Skip,

    /// <summary>
    /// Insert the raw OMML XML as a comment.
    /// </summary>
    InsertOmmlComment
}
