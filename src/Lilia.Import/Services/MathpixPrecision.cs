using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lilia.Import.Services;

/// <summary>How sure the reader was of one page: the score (0–1), its least sure line, and whether it is under the threshold.</summary>
public sealed record PagePrecision(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("min")] double Min,
    [property: JsonPropertyName("lines")] int Lines,
    [property: JsonPropertyName("chars")] int Chars,
    [property: JsonPropertyName("low")] bool Low);

/// <summary>
/// The precision score (21 Nov 2026, the owner: "how sure the OCR is, so authors and we know which pages to check"),
/// from Mathpix's lines.json (<c>GET /v3/pdf/{pdf_id}.lines.json</c>; docs.mathpix.com/reference/post-v3-pdf, "lines
/// data"):
/// <list type="bullet">
/// <item><c>pages[]</c>, each with <c>page</c> (from 1) and <c>lines[]</c>;</item>
/// <item>each line's <c>confidence_rate</c>: "estimated confidence of output quality (geometric mean of per token OCR
/// confidence)", 0–1; else its <c>confidence</c>: "estimated probability 100% correct (product of per token OCR
/// confidence)", which a long line drives toward 0, so it is only the fallback;</item>
/// <item>each line's <c>text</c> ("searchable text, empty string for page elements that do not necessarily have
/// associated text"), else its <c>text_display</c>: the weight.</item>
/// </list>
/// A page's score is the character-weighted mean of its lines' scores; lines with no text or no score don't count, and a
/// page with none has no score (left out, not 0). The file's score is the mean of its pages' (each page once).
/// </summary>
public static class MathpixPrecision
{
    /// <summary>A page under this is one to check against the scan (<c>BulkImport:LowConfidence</c>).</summary>
    public const double DefaultLowThreshold = 0.80;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>
    /// Each page's precision in <paramref name="linesJson"/>, its number moved by <paramref name="pageOffset"/> (a chunk's
    /// pages are 1… in Mathpix's reading; the file's are its first page − 1 further on). Throws <see cref="JsonException"/>
    /// on JSON that isn't.
    /// </summary>
    public static IReadOnlyList<PagePrecision> FromLinesJson(string linesJson, int pageOffset = 0, double lowThreshold = DefaultLowThreshold)
    {
        using var doc = JsonDocument.Parse(linesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<PagePrecision>();
        var position = 0;
        foreach (var page in pages.EnumerateArray())
        {
            position++;
            if (page.ValueKind != JsonValueKind.Object) continue;
            var number = page.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : position;
            if (!page.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array) continue;
            double weighted = 0, min = 1;
            long chars = 0;
            var counted = 0;
            foreach (var line in lines.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.Object || ScoreOf(line) is not { } score) continue;
                var length = TextOf(line).Length;
                if (length == 0) continue;
                weighted += score * length;
                chars += length;
                counted++;
                min = Math.Min(min, score);
            }
            if (chars == 0) continue;
            var mean = weighted / chars;
            result.Add(new PagePrecision(number + pageOffset, Math.Round(mean, 3), Math.Round(min, 3), counted,
                (int)Math.Min(int.MaxValue, chars), mean < lowThreshold));
        }
        return result;
    }

    /// <summary>A line's score: <c>confidence_rate</c>, else <c>confidence</c>, when a number in 0–1.</summary>
    private static double? ScoreOf(JsonElement line)
    {
        foreach (var name in (string[])["confidence_rate", "confidence"])
            if (line.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
                && d is >= 0 and <= 1 && !double.IsNaN(d))
                return d;
        return null;
    }

    private static string TextOf(JsonElement line)
    {
        foreach (var name in (string[])["text", "text_display"])
            if (line.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s
                && s.Trim() is { Length: > 0 } t)
                return t;
        return "";
    }

    /// <summary>The mean of the pages' scores (each page once), or null when none has one.</summary>
    public static double? Mean(IReadOnlyCollection<PagePrecision> pages) =>
        pages.Count == 0 ? null : Math.Round(pages.Average(p => p.Score), 3);

    /// <summary>The pages as stored (<c>bulk_import_chunks.precision_pages</c>).</summary>
    public static string Serialize(IReadOnlyList<PagePrecision> pages) => JsonSerializer.Serialize(pages, Json);

    /// <summary>The stored pages back; empty for null or JSON that isn't a list of pages.</summary>
    public static IReadOnlyList<PagePrecision> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<PagePrecision>>(stored, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The warning a page the reader was unsure of gives the author.</summary>
    public static string LowPageWarning(PagePrecision page) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Page {page.Page}: the reader was unsure (precision {page.Score:0.00}). Check it against the scan");
}
