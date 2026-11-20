using System.Text.Json.Serialization;

namespace Lilia.Import.Models;

public class MathpixPdfRequest
{
    [JsonPropertyName("conversion_formats")]
    public MathpixConversionFormats ConversionFormats { get; set; } = new();

    [JsonPropertyName("math_inline_delimiters")]
    public string[] MathInlineDelimiters { get; set; } = ["$", "$"];

    [JsonPropertyName("math_display_delimiters")]
    public string[] MathDisplayDelimiters { get; set; } = ["$$", "$$"];

    [JsonPropertyName("rm_spaces")]
    public bool RmSpaces { get; set; } = true;

    [JsonPropertyName("include_equation_tags")]
    public bool IncludeEquationTags { get; set; } = true;

    [JsonPropertyName("enable_tables_fallback")]
    public bool EnableTablesFallback { get; set; } = true;

    /// <summary>
    /// <c>metadata.improve_mathpix: false</c>: Mathpix doesn't use the file to improve its service, nothing derived
    /// from it is kept on disk, and the uploaded document is deleted as soon as it is processed
    /// (docs.mathpix.com/concepts/privacy and /concepts/data-retention; PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    [JsonPropertyName("metadata")]
    public MathpixRequestMetadata Metadata { get; set; } = new();
}

/// <summary>
/// <c>POST /v3/text</c>'s options, for one scanned page (a photo of handwritten notes): Mathpix Markdown out, with the
/// same math delimiters and spacing as the PDF route, so the same Markdown reader takes it; <c>metadata.improve_mathpix</c>
/// false, as on every Mathpix call (docs.mathpix.com/reference/post-v3-text and /concepts/privacy; PDF and scan import,
/// 20 Nov 2026).
/// </summary>
public class MathpixTextRequest
{
    [JsonPropertyName("formats")]
    public string[] Formats { get; set; } = ["text"];

    [JsonPropertyName("math_inline_delimiters")]
    public string[] MathInlineDelimiters { get; set; } = ["$", "$"];

    [JsonPropertyName("math_display_delimiters")]
    public string[] MathDisplayDelimiters { get; set; } = ["$$", "$$"];

    [JsonPropertyName("rm_spaces")]
    public bool RmSpaces { get; set; } = true;

    [JsonPropertyName("metadata")]
    public MathpixRequestMetadata Metadata { get; set; } = new();
}

/// <summary><c>POST /v3/text</c>'s answer: the page as Mathpix Markdown, or an error (PDF and scan import, 20 Nov 2026).</summary>
public class MathpixTextResponse
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonPropertyName("is_handwritten")]
    public bool? IsHandwritten { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_info")]
    public MathpixErrorInfo? ErrorInfo { get; set; }
}

public class MathpixRequestMetadata
{
    [JsonPropertyName("improve_mathpix")]
    public bool ImproveMathpix { get; set; } = false;
}

public class MathpixConversionFormats
{
    [JsonPropertyName("md")]
    public bool Md { get; set; } = true;

    /// <summary>LaTeX with its images in one zip (PDF import via tex.zip, 19 Nov 2026). Left out when null.</summary>
    [JsonPropertyName("tex.zip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TexZip { get; set; }
}

/// <summary>
/// <c>GET /v3/converter/{pdf_id}</c>: the conversions of a read PDF, one entry per requested format, each
/// received, loaded, processing, completed or error (docs.mathpix.com/reference/shared-types; PDF import via
/// tex.zip, 19 Nov 2026).
/// </summary>
public class MathpixConverterStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("conversion_status")]
    public Dictionary<string, MathpixFormatStatus>? ConversionStatus { get; set; }
}

public class MathpixFormatStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("error_info")]
    public MathpixErrorInfo? ErrorInfo { get; set; }
}

public class MathpixErrorInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class MathpixPdfResponse
{
    [JsonPropertyName("pdf_id")]
    public string PdfId { get; set; } = "";
}

public class MathpixPdfStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("num_pages")]
    public int? NumPages { get; set; }

    [JsonPropertyName("num_pages_completed")]
    public int? NumPagesCompleted { get; set; }

    [JsonPropertyName("percent_done")]
    public double? PercentDone { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("md")]
    public string? Markdown { get; set; }
}
