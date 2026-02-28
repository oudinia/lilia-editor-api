using System.Text.Json.Serialization;

namespace Lilia.Import.Models;

/// <summary>
/// Top-level response from MinerU's /file_parse endpoint.
/// Structure: { backend, version, results: { "filename": { md_content, content_list, images } } }
/// </summary>
public class MineruApiResponse
{
    [JsonPropertyName("backend")]
    public string Backend { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public Dictionary<string, MineruFileResult> Results { get; set; } = new();
}

/// <summary>
/// Per-file result from MinerU. Note: content_list is a JSON string, not a parsed array.
/// </summary>
public class MineruFileResult
{
    [JsonPropertyName("md_content")]
    public string? MdContent { get; set; }

    /// <summary>
    /// JSON-encoded string of List&lt;MineruContentBlock&gt;. Must be deserialized separately.
    /// </summary>
    [JsonPropertyName("content_list")]
    public string? ContentListJson { get; set; }

    [JsonPropertyName("images")]
    public Dictionary<string, string> Images { get; set; } = new();
}

/// <summary>
/// Parsed content list and images for consumption by PdfImportService.
/// </summary>
public class MineruParseResponse
{
    public List<MineruContentBlock> ContentList { get; set; } = [];
    public Dictionary<string, string> Images { get; set; } = new();
}

/// <summary>
/// Options passed to MinerU's /file_parse endpoint to control parsing behavior.
/// </summary>
public class MineruParseOptions
{
    /// <summary>
    /// Language list for OCR (e.g., "en", "ch"). Default: "en".
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Whether to enable formula/equation detection and parsing.
    /// Disabling significantly reduces parse time for text-heavy documents.
    /// </summary>
    public bool FormulaEnable { get; set; } = true;

    /// <summary>
    /// Whether to enable table structure recognition.
    /// Disabling reduces parse time when tables are not needed.
    /// </summary>
    public bool TableEnable { get; set; } = true;

    /// <summary>
    /// Zero-based start page index (inclusive). Null = start from first page.
    /// </summary>
    public int? StartPageId { get; set; }

    /// <summary>
    /// Zero-based end page index (inclusive). Null = parse to last page.
    /// </summary>
    public int? EndPageId { get; set; }
}

/// <summary>
/// A single content block from MinerU's parsed output.
/// </summary>
public class MineruContentBlock
{
    /// <summary>
    /// Block type: "text", "equation", "table", "image", "code", "list"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Text content of the block. For equations, contains LaTeX.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Heading level: 0 = regular text, 1+ = heading level.
    /// Only present when type is "text".
    /// </summary>
    [JsonPropertyName("text_level")]
    public int? TextLevel { get; set; }

    /// <summary>
    /// Bounding box [x0, y0, x1, y1] on the source page.
    /// </summary>
    [JsonPropertyName("bbox")]
    public List<double>? Bbox { get; set; }

    /// <summary>
    /// Zero-based page index where this block appears.
    /// </summary>
    [JsonPropertyName("page_idx")]
    public int? PageIdx { get; set; }

    /// <summary>
    /// Relative path to the extracted image file (when type is "image").
    /// </summary>
    [JsonPropertyName("img_path")]
    public string? ImgPath { get; set; }

    /// <summary>
    /// Caption detected for an image block.
    /// </summary>
    [JsonPropertyName("image_caption")]
    public string? ImageCaption { get; set; }

    /// <summary>
    /// HTML table markup (when type is "table").
    /// </summary>
    [JsonPropertyName("table_body")]
    public string? TableBody { get; set; }
}
