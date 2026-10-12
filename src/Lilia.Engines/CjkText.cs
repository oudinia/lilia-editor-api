using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Chinese, Japanese or Korean text. pdflatex can't print it with this server's fonts (MCP-26:
/// one <c>数</c> failed the whole document), so a document holding any is compiled with xelatex
/// and <c>xeCJK</c> over the Fandol fonts TeX Live ships (see <see cref="LaTeXPreamble.EngineAddendum"/>).
/// </summary>
public static class CjkText
{
    // Han (with extension A and compatibility ideographs), kana, Hangul, CJK punctuation and
    // full-width forms.
    private static readonly Regex Cjk = new(
        @"[　-〿぀-ゟ゠-ヿ㐀-䶿一-鿿가-힯ᄀ-ᇿ豈-﫿＀-￯]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool Contains(string? text) => !string.IsNullOrEmpty(text) && Cjk.IsMatch(text);

    /// <summary>Any string in a block's content (its raw JSON may hold the text as \u escapes).</summary>
    public static bool Contains(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => Contains(content.GetString()),
        JsonValueKind.Object => content.EnumerateObject().Any(p => Contains(p.Value)),
        JsonValueKind.Array => content.EnumerateArray().Any(Contains),
        _ => false,
    };
}
