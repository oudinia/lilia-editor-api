using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

public interface IMathpixClient
{
    Task<string> SubmitPdfAsync(string filePath, CancellationToken ct = default);
    Task<MathpixPdfStatus> GetStatusAsync(string pdfId, CancellationToken ct = default);
    Task<string> WaitForCompletionAsync(string pdfId, CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<byte[]> GetImageAsync(string imageUrl, CancellationToken ct = default);

    /// <summary>
    /// Waits for the PDF's tex.zip conversion (<c>GET /v3/converter/{pdf_id}</c>) and downloads it
    /// (<c>GET /v3/pdf/{pdf_id}.tex.zip</c>). Throws when the conversion fails or takes longer than
    /// <c>TexZipTimeoutSeconds</c> (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    Task<byte[]> GetTexZipAsync(string pdfId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the PDF and its outputs at Mathpix (<c>DELETE /v3/pdf/{pdf_id}</c>). True when Mathpix said yes
    /// (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    Task<bool> DeletePdfAsync(string pdfId, CancellationToken ct = default);
}
