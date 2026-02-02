using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for parsing DOCX files into the intermediate ImportDocument model.
/// This is Stage 1 of the two-stage import process.
/// </summary>
public interface IDocxParser
{
    /// <summary>
    /// Parse a DOCX file and extract content into the intermediate model.
    /// </summary>
    /// <param name="filePath">Path to the DOCX file.</param>
    /// <param name="options">Import options.</param>
    /// <returns>The parsed ImportDocument containing all extracted elements.</returns>
    Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null);

    /// <summary>
    /// Check if a file can be parsed (by extension).
    /// </summary>
    /// <param name="filePath">Path to check.</param>
    /// <returns>True if the file appears to be a DOCX file.</returns>
    bool CanParse(string filePath);
}
