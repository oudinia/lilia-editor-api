using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// Letters pdflatex can't print with Lilia's preamble, and LuaLaTeX prints with the Unicode faces it loads under fontspec
/// (<see cref="LaTeXPreamble.UnicodeFaces"/>: Computer Modern Unicode, Latin, Cyrillic and Greek): any Cyrillic letter, and
/// a Greek one the shared preamble doesn't map onto maths (<see cref="LaTeXPreamble.UnicodeShims"/> maps α to
/// <c>\ensuremath{\alpha}</c>, so "γ rays" stays on pdflatex; ά or ῥ has no such mapping).
///
/// <para>21 Nov 2026, the launch bot's bulk import of Russian lecture notes (Rostov): Mathpix read real Cyrillic, and all
/// three documents stopped ("Unicode character п (U+043F) not set up for use with LaTeX"). The document's language is read
/// from its text (<see cref="DocumentLanguage.Detect(string)"/>), and Cyrillic is Russian only when it is most of the
/// letters (30 %); a scan's Cyrillic mixed with maths and Latin OCR, or a Russian quotation in an English paper, is less,
/// so the document stayed English, on pdflatex. A document holding any such letter is compiled with LuaLaTeX, as one
/// holding any Chinese, Japanese or Korean is compiled with XeLaTeX (<see cref="CjkText"/>), whatever its language, so
/// documents written by hand get it as imports do. Its language is still the one its text reads as: English stays
/// English, with Russian words in it.</para>
/// </summary>
public static class UnicodeFacesText
{
    /// <summary>The Greek letters the shared preamble maps for pdflatex.</summary>
    private static readonly HashSet<int> Shimmed = Regex
        .Matches(LaTeXPreamble.UnicodeShims, @"\\newunicodechar\{(.+?)\}\{")
        .Select(m => m.Groups[1].Value)
        .Where(c => c.Length > 0)
        .Select(c => char.ConvertToUtf32(c, 0))
        .ToHashSet();

    /// <summary>Whether <paramref name="c"/> needs LuaLaTeX and its Unicode faces.</summary>
    public static bool Needs(char c) =>
        c is >= '\u0400' and <= '\u052F'                 // Cyrillic and its supplement
            or >= '\u1C80' and <= '\u1C8F' or >= '\u2DE0' and <= '\u2DFF' or >= '\uA640' and <= '\uA69F'
        || (c is >= '\u0370' and <= '\u03FF' or >= '\u1F00' and <= '\u1FFF') && char.IsLetter(c) && !Shimmed.Contains(c);

    public static bool Contains(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
            if (Needs(c)) return true;
        return false;
    }

    /// <summary>Any string in a block's content.</summary>
    public static bool Contains(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => Contains(content.GetString()),
        JsonValueKind.Object => content.EnumerateObject().Any(p => Contains(p.Value)),
        JsonValueKind.Array => content.EnumerateArray().Any(Contains),
        _ => false,
    };

    /// <summary>The document's title or any of its blocks.</summary>
    public static bool Contains(Document doc) =>
        Contains(doc.Title) || (doc.Blocks ?? new List<Block>()).Any(b => Contains(b.Content.RootElement));
}
