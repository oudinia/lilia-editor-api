using Lilia.Core.Models;

namespace Lilia.Import.Models;

/// <summary>
/// Result of importing a DOCX file and converting to Lilia format.
/// </summary>
public class ImportResult
{
    /// <summary>
    /// The converted Lilia document (null if import failed).
    /// </summary>
    public Document? Document { get; set; }

    /// <summary>
    /// Sections created from headings.
    /// </summary>
    public List<Section> Sections { get; set; } = [];

    /// <summary>
    /// All warnings generated during import and conversion.
    /// </summary>
    public List<ImportWarning> Warnings { get; set; } = [];

    /// <summary>
    /// Whether the import was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if import failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The intermediate representation (for debugging or review).
    /// </summary>
    public ImportDocument? IntermediateDocument { get; set; }

    /// <summary>
    /// Statistics about the import.
    /// </summary>
    public ImportStatistics Statistics { get; set; } = new();

    /// <summary>
    /// Creates a successful import result.
    /// </summary>
    public static ImportResult Successful(Document document, List<Section> sections, List<ImportWarning> warnings)
    {
        return new ImportResult
        {
            Success = true,
            Document = document,
            Sections = sections,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Creates a failed import result.
    /// </summary>
    public static ImportResult Failed(string errorMessage, List<ImportWarning>? warnings = null)
    {
        return new ImportResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Warnings = warnings ?? []
        };
    }
}

/// <summary>
/// Statistics about an import operation.
/// </summary>
public class ImportStatistics
{
    /// <summary>
    /// Total elements parsed from DOCX.
    /// </summary>
    public int TotalElementsParsed { get; set; }

    /// <summary>
    /// Number of sections created.
    /// </summary>
    public int SectionsCreated { get; set; }

    /// <summary>
    /// Number of blocks created.
    /// </summary>
    public int BlocksCreated { get; set; }

    /// <summary>
    /// Number of equations found.
    /// </summary>
    public int EquationsFound { get; set; }

    /// <summary>
    /// Number of equations successfully converted to LaTeX.
    /// </summary>
    public int EquationsConverted { get; set; }

    /// <summary>
    /// Number of images extracted.
    /// </summary>
    public int ImagesExtracted { get; set; }

    /// <summary>
    /// Number of tables extracted.
    /// </summary>
    public int TablesExtracted { get; set; }

    /// <summary>
    /// Number of code blocks detected.
    /// </summary>
    public int CodeBlocksDetected { get; set; }

    /// <summary>
    /// Time taken to parse the DOCX.
    /// </summary>
    public TimeSpan ParseTime { get; set; }

    /// <summary>
    /// Time taken to convert to Lilia format.
    /// </summary>
    public TimeSpan ConvertTime { get; set; }

    /// <summary>
    /// Total import time.
    /// </summary>
    public TimeSpan TotalTime => ParseTime + ConvertTime;
}
