using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Parses a PDF file into the intermediate ImportDocument representation
/// using the MinerU service as the PDF extraction backend.
/// </summary>
public interface IPdfParser
{
    /// <summary>
    /// Parse a PDF file and return the intermediate document model.
    /// </summary>
    Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the given file can be parsed (i.e., is a .pdf file).
    /// </summary>
    bool CanParse(string filePath);
}
