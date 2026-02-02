using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for the DOCX import service that orchestrates the full import process.
/// </summary>
public interface IDocxImportService
{
    /// <summary>
    /// Import a DOCX file and convert it to Lilia format.
    /// </summary>
    /// <param name="filePath">Path to the DOCX file.</param>
    /// <param name="importOptions">Options for parsing the DOCX file.</param>
    /// <param name="conversionOptions">Options for converting to Lilia format.</param>
    /// <returns>The import result containing the document, sections, and any warnings.</returns>
    Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions? importOptions = null,
        ConversionOptions? conversionOptions = null);

    /// <summary>
    /// Parse a DOCX file into the intermediate representation without converting.
    /// Useful for previewing or reviewing import data before conversion.
    /// </summary>
    /// <param name="filePath">Path to the DOCX file.</param>
    /// <param name="options">Options for parsing the DOCX file.</param>
    /// <returns>The intermediate ImportDocument.</returns>
    Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null);

    /// <summary>
    /// Convert an already-parsed ImportDocument to Lilia format.
    /// </summary>
    /// <param name="importDocument">The intermediate representation.</param>
    /// <param name="options">Options for conversion.</param>
    /// <returns>The import result.</returns>
    ImportResult Convert(ImportDocument importDocument, ConversionOptions? options = null);

    /// <summary>
    /// Check if a file can be imported.
    /// </summary>
    /// <param name="filePath">Path to check.</param>
    /// <returns>True if the file appears to be a supported DOCX file.</returns>
    bool CanImport(string filePath);

    /// <summary>
    /// Get supported file extensions.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }
}
