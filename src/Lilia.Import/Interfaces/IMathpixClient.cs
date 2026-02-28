using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

public interface IMathpixClient
{
    Task<string> SubmitPdfAsync(string filePath, CancellationToken ct = default);
    Task<MathpixPdfStatus> GetStatusAsync(string pdfId, CancellationToken ct = default);
    Task<string> WaitForCompletionAsync(string pdfId, CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<byte[]> GetImageAsync(string imageUrl, CancellationToken ct = default);
}
