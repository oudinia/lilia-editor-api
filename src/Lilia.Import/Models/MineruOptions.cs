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
    /// MinerU parsing backend.
    /// - "pipeline": CPU-compatible, uses ONNX models sequentially (accuracy ~82)
    /// - "vlm-auto-engine": VLM backend, requires GPU 8GB+ VRAM (accuracy ~90+)
    /// - "hybrid-auto-engine": Combines VLM + pipeline fallback, requires GPU 10GB+ VRAM (accuracy ~90+, default since MinerU 2.7)
    /// </summary>
    public string Backend { get; set; } = "pipeline";

    /// <summary>
    /// Maximum time in seconds to wait for MinerU to process a PDF.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum allowed file size in megabytes.
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 50;

    /// <summary>
    /// Default language for OCR (e.g., "en", "ch").
    /// MinerU defaults to "ch" (Chinese) if not specified; we default to "en".
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Whether formula/equation detection is enabled by default.
    /// Disabling significantly reduces parse time for text-heavy documents.
    /// </summary>
    public bool FormulaEnable { get; set; } = true;

    /// <summary>
    /// Whether table structure recognition is enabled by default.
    /// Disabling reduces parse time when tables are not needed.
    /// </summary>
    public bool TableEnable { get; set; } = true;

    /// <summary>
    /// Number of pages per batch when using batched parsing.
    /// Set to 0 to disable batching and parse the entire PDF in a single request.
    /// Default: 10
    /// </summary>
    public int BatchPageSize { get; set; } = 10;
}
