using System.Globalization;

namespace Lilia.Api.Services;

/// <summary>
/// What kind of thing a character that won't compile is — which decides what
/// the paste chip can honestly offer for it.
///
/// <para><b>symbol</b> — could have a LaTeX command (<c>ℵ</c> is <c>\aleph</c>),
/// so <i>Look up</i> is worth offering even when the catalog has no row yet.
/// <b>script</b> — a letter of running text in a writing system pdflatex's fonts
/// don't carry (<c>中</c>, <c>ạ</c>, <c>ש</c>). <b>emoji</b> — a pictograph. For
/// the last two no command exists: they need a font or an engine, and a lookup
/// would dead-end, so the chip says so and offers nothing.</para>
///
/// <para>Decided by block and general category, on the server with the rest of
/// the rules (Olivia, 25 Sep). The borderline calls, each deliberate:
/// letterlike symbols (<c>ℵ ℏ ℘</c>) and mathematical alphanumerics
/// (<c>𝔸 𝒞</c>) are letters by category but symbols in use; Greek is a symbol
/// because authors paste it as maths, and an accented Greek letter that lands on
/// Symbols' empty state is a dead end, not a false statement; CJK punctuation
/// (<c>。</c>) is script, because it arrives with Chinese text and no command
/// will set it; dingbats (<c>✓</c>) are symbols, because many have commands.</para>
/// </summary>
public static class UnicodeKind
{
    public const string Symbol = "symbol";
    public const string Script = "script";
    public const string Emoji = "emoji";

    public static string Of(int codepoint)
    {
        if (IsEmoji(codepoint)) return Emoji;
        if (IsSymbolBlock(codepoint)) return Symbol;
        if (IsCjkBlock(codepoint)) return Script;

        return CharUnicodeInfo.GetUnicodeCategory(codepoint) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.DecimalDigitNumber => Script,
            _ => Symbol,
        };
    }

    private static bool IsEmoji(int cp) =>
        cp is >= 0x1F000 and <= 0x1FAFF      // mahjong … symbols & pictographs ext-A, flags, skin tones
            or 0x200D or 0xFE0F or 0x20E3    // joiner, emoji presentation, keycap
            or >= 0xE0020 and <= 0xE007F;    // tag sequences (subdivision flags)

    private static bool IsSymbolBlock(int cp) =>
        cp is >= 0x0370 and <= 0x03FF        // Greek and Coptic
            or >= 0x2100 and <= 0x214F       // Letterlike Symbols
            or >= 0x1D400 and <= 0x1D7FF;    // Mathematical Alphanumeric Symbols

    private static bool IsCjkBlock(int cp) =>
        cp is >= 0x2E80 and <= 0x2FDF        // radicals, Kangxi
            or >= 0x3000 and <= 0x33FF       // CJK punctuation, kana, bopomofo, enclosed/compat
            or >= 0x3400 and <= 0x4DBF       // ext A
            or >= 0x4E00 and <= 0x9FFF       // unified ideographs
            or >= 0xAC00 and <= 0xD7AF       // Hangul syllables
            or >= 0xF900 and <= 0xFAFF       // compatibility ideographs
            or >= 0xFE30 and <= 0xFE4F       // compatibility forms
            or >= 0xFF00 and <= 0xFFEF       // half- and fullwidth forms
            or >= 0x20000 and <= 0x3FFFF;    // ext B onward
}
