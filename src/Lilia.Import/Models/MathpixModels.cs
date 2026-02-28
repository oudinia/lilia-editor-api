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
}

public class MathpixConversionFormats
{
    [JsonPropertyName("md")]
    public bool Md { get; set; } = true;
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
