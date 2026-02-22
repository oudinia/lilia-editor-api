namespace Lilia.Import.Models;

/// <summary>
/// Configuration options for connecting to the MinerU PDF parsing service.
/// </summary>
public class MineruOptions
{
    /// <summary>
    /// Base URL of the MinerU API (e.g., "http://localhost:8000").
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Maximum time in seconds to wait for MinerU to process a PDF.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum allowed file size in megabytes.
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 50;
}
