using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// HTTP client for communicating with the MinerU PDF parsing sidecar.
/// </summary>
public interface IMineruClient
{
    /// <summary>
    /// Send a PDF file to MinerU for parsing and return the structured content.
    /// </summary>
    Task<MineruParseResponse> ParsePdfAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch an extracted image from MinerU by its relative path.
    /// </summary>
    Task<byte[]> GetImageAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the MinerU service is reachable and healthy.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
