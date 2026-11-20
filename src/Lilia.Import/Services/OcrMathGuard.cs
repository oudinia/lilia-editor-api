using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// A character that OCR put inside a formula and that no maths can contain: a letter or mark from Arabic, Hebrew, CJK,
/// Indic or another script outside Latin and Greek. The launch bot's handwritten page (20 Nov 2026): Mathpix read a
/// symbol as خ (U+062E) inside a formula, and pdfLaTeX stopped the whole PDF ("Unicode character خ (U+062E)"). In maths
/// such a character becomes a visible <see cref="Placeholder"/> and the import says so; text outside maths is never
/// touched (a real Arabic or Chinese text compiles with XeLaTeX). Used on Mathpix's output only (scan and PDF import).
/// </summary>
public static partial class OcrMathGuard
{
    /// <summary>A box, in maths or in text (amssymb, in every Lilia preamble and in Mathpix's).</summary>
    public const string Placeholder = @"\ensuremath{\square}";

    /// <summary>A formula's LaTeX (an equation block): every unsettable character replaced, a run of them by one box.</summary>
    public static string InMath(string latex, ICollection<string> replaced) => Replace(latex, replaced);

    /// <summary>
    /// Text that may hold maths (<c>$…$</c>, <c>$$…$$</c>, <c>\(…\)</c>, <c>\[…\]</c>, a maths environment): only what
    /// is inside the maths is changed.
    /// </summary>
    public static string InText(string text, ICollection<string> replaced)
    {
        if (string.IsNullOrEmpty(text) || !HasUnsettable(text)) return text;
        return MathRegion().Replace(text, m =>
        {
            var body = m.Groups["body"];
            return m.Value[..(body.Index - m.Index)] + Replace(body.Value, replaced) + m.Value[(body.Index - m.Index + body.Length)..];
        });
    }

    /// <summary>Whether the text has any character that would be replaced if it were in maths.</summary>
    public static bool HasUnsettable(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (Unsettable(rune)) return true;
        return false;
    }

    private static string Replace(string math, ICollection<string> replaced)
    {
        if (string.IsNullOrEmpty(math) || !HasUnsettable(math)) return math;
        var sb = new StringBuilder(math.Length);
        var inRun = false;
        foreach (var rune in math.EnumerateRunes())
        {
            if (Unsettable(rune))
            {
                replaced.Add($"U+{rune.Value:X4} ({rune})");
                if (!inRun) sb.Append(Placeholder).Append(' ');
                inRun = true;
                continue;
            }
            inRun = false;
            sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// A letter or mark (no case: Lo, Lm, Mn, Mc, Me) outside the blocks maths uses: Latin and its extensions, IPA, the
    /// combining diacritics (x⃗'s arrow), Greek, Cyrillic, general punctuation through the maths and technical symbols
    /// (ℵ is a letter there), and the mathematical alphanumerics.
    /// </summary>
    internal static bool Unsettable(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is not (UnicodeCategory.OtherLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            return false;
        var c = rune.Value;
        return !(c <= 0x052F || c is >= 0x1D00 and <= 0x1FFF || c is >= 0x2000 and <= 0x2BFF || c is >= 0x2C60 and <= 0x2C7F
            || c is >= 0xA720 and <= 0xA7FF || c is >= 0xFE00 and <= 0xFE0F || c is >= 0x1D400 and <= 0x1D7FF);
    }

    [GeneratedRegex(@"(?<!\\)\$\$(?<body>.+?)(?<!\\)\$\$|\\\[(?<body>.+?)\\\]|\\\((?<body>.+?)\\\)|\\begin\{(?<env>equation|align|gather|multline|eqnarray|displaymath|math|flalign|alignat)(?<star>\*?)\}(?<body>.+?)\\end\{\k<env>\k<star>\}|(?<![\\$])\$(?!\$)(?<body>.+?)(?<![\\])\$", RegexOptions.Singleline)]
    private static partial Regex MathRegion();
}
