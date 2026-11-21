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
    /// The PDF's line-by-line data (<c>GET /v3/pdf/{pdf_id}.lines.json</c>), raw: per page (<c>pages[].page</c>, from 1)
    /// its lines (<c>pages[].lines[]</c>) with <c>text</c>, <c>confidence</c> and <c>confidence_rate</c> (0–1). Always
    /// asked of api.mathpix.com, whatever <c>Mathpix:BaseUrl</c> says (the precision score, 21 Nov 2026). Throws when
    /// Mathpix doesn't answer 200 or the data is over 20 MB.
    /// </summary>
    Task<string> GetLinesJsonAsync(string pdfId, CancellationToken ct = default);

}
