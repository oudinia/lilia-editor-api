using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// A heading's printed text and its label, read the same way by both emitters: the label field,
/// then the LML id, then a Markdown anchor still at the end of the text (<c>Results {#sec:res}</c>).
/// Headings parsed before the LML parser kept the anchor as a label printed it in the title, and
/// every reference to them dangled (13 Oct sweep, LTX-11). The exporter also ignored the id.
/// </summary>
public static class HeadingAnchor
{
    private static readonly Regex Trailing = new(@"\s*\{#(?<k>[^\s{}]+)\}\s*$", RegexOptions.Compiled);

    private static readonly Regex BakedNumber = new(@"^(?:\d+(?:\.\d+)*\.?\s+|[IVXLC]+\.\s+|[A-Z]\.\s+)(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// A number typed into the heading ("1. Introduction", "1.2 Scope", "IV. Methods") is dropped:
    /// LaTeX numbers the heading itself, and the PDF and the table of contents showed both. The
    /// import does the same (SectionKeywordRegistry.StripNumberingPrefix).
    /// </summary>
    public static string WithoutTypedNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var m = BakedNumber.Match(text);
        return m.Success ? m.Groups[1].Value : text;
    }

    public static (string Text, string? Label) Read(JsonElement content, string text)
    {
        static string? Str(JsonElement c, string name) =>
            c.ValueKind == JsonValueKind.Object && c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

        var label = Str(content, "label") ?? Str(content, "id");
        var anchor = Trailing.Match(text ?? "");
        if (!anchor.Success) return (text ?? "", label);
        return (text![..anchor.Index], label ?? anchor.Groups["k"].Value);
    }
}
