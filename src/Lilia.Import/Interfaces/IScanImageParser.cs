using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Reads one scanned page (a JPEG or PNG: a photo of handwritten maths notes, a phone scan) into the import's
/// intermediate document, as a PDF's Markdown is read. Mathpix's <c>POST /v3/text</c> behind it
/// (<see cref="Services.MathpixPdfImportService"/>); only registered when the PDF parser is Mathpix (PDF and scan import,
/// 20 Nov 2026).
/// </summary>
public interface IScanImageParser
{
    /// <param name="filePath">The image on disk.</param>
    /// <param name="contentType"><c>image/jpeg</c> or <c>image/png</c>, from the file's own bytes.</param>
    Task<ImportDocument> ParseImageAsync(string filePath, string contentType, CancellationToken cancellationToken = default);
}
