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
    /// <c>TexZipTimeoutSeconds</c>, or than <paramref name="maxWait"/> when that is shorter (the import's deadline)
    /// (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    Task<byte[]> GetTexZipAsync(string pdfId, CancellationToken ct = default, TimeSpan? maxWait = null);

    /// <summary>
    /// Deletes the PDF and its outputs at Mathpix (<c>DELETE /v3/pdf/{pdf_id}</c>). True when Mathpix said yes
    /// (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    Task<bool> DeletePdfAsync(string pdfId, CancellationToken ct = default);

    /// <summary>
    /// Reads one scanned page (a JPEG or PNG, e.g. a photo of handwritten notes) with <c>POST /v3/text</c>, sent as a
    /// multipart upload with <c>options_json</c>, and returns its Mathpix Markdown. One synchronous call: no polling, and no
    /// pdf_id to delete afterwards (PDF and scan import, 20 Nov 2026).
    /// </summary>
    Task<MathpixTextResponse> ReadImageAsync(string filePath, string contentType, CancellationToken ct = default);
}
