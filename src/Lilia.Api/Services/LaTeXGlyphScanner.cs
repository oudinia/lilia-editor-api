using System.Text;
using System.Text.RegularExpressions;
using Lilia.Engines;

namespace Lilia.Api.Services;

/// <summary>
/// Finds characters LaTeX silently dropped.
///
/// A missing glyph is NOT an error in TeX. When the current font has no
/// character for a code point, TeX drops the character, writes a note in the
/// log, and carries on — exit code 0, valid PDF, text gone. So a document
/// containing Chinese, Hebrew, Arabic or accented text can "compile
/// successfully" with that text deleted.
///
/// The obvious alternative check does not work either: extracting text and
/// looking for the characters fails, because pdftotext returns nothing for CJK
/// even when the PDF is perfect. The log is the only reliable signal.
///
/// Mirrors Lilia.Latex.Core.TexLogParser in the lilia-latex-service repo.
/// Duplicated deliberately: a path-based ProjectReference across repositories
/// breaks on clone and in CI, and this is ~30 lines. Unify via a package feed
/// if a second consumer appears.
/// </summary>
public static partial class LaTeXGlyphScanner
{
    /// <summary>
    /// TeX's <c>max_print_line</c>. The log is hard-wrapped at this many BYTES —
    /// not characters — mid-token, with no continuation marker of any kind.
    /// </summary>
    private const int MaxPrintLine = 79;

    /// <summary>One code point the current font could not render.</summary>
    /// <param name="Character">The literal character, e.g. "中".</param>
    /// <param name="CodePoint">Unicode scalar as TeX wrote it, e.g. "4E2D".</param>
    /// <param name="Font">Font that lacked it; null when the log did not say.</param>
    public record DroppedGlyph(string Character, string CodePoint, string? Font);

    // The character can be any single Unicode scalar including astral-plane
    // ones, so match non-greedily up to the code point rather than assuming BMP.
    // The font group is optional: TeX wraps the line mid-name, so on a raw log
    // the closing quote is on the NEXT physical line (see Unwrap).
    [GeneratedRegex(
        """Missing character: There is no (?<ch>.+?) \(U\+(?<cp>[0-9A-Fa-f]+)\)(?: in font "?(?<font>[^"\n]*)"?)?""",
        RegexOptions.CultureInvariant)]
    private static partial Regex MissingCharRegex();

    /// <summary>
    /// Rejoins lines TeX split at <see cref="MaxPrintLine"/>. A real line from a
    /// failing CJK document looks like this:
    ///
    /// <code>
    /// Missing character: There is no 中 (U+4E2D) in font "name:Latin Modern Roman:mod
    /// e=node;script=latn;language=dflt;+tlig;"!
    /// </code>
    ///
    /// The font name straddles the break. Only the font group needs this — the
    /// character and its code point always sit well before the wrap point.
    /// </summary>
    private static string Unwrap(string rawLog)
    {
        var sb = new StringBuilder(rawLog.Length);
        var lines = rawLog.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i]);
            var wrapped = Encoding.UTF8.GetByteCount(lines[i]) >= MaxPrintLine;
            if (!wrapped && i < lines.Length - 1) sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Distinct code points the compile dropped, in code point order.
    ///
    /// Distinct on purpose: TeX emits one line per OCCURRENCE, so a paragraph of
    /// Chinese produces hundreds. The number worth showing a human is "this font
    /// covers none of the 40 characters you used", not "312 warnings".
    /// </summary>
    public static IReadOnlyList<DroppedGlyph> Scan(string? rawLog)
    {
        if (string.IsNullOrEmpty(rawLog) || !rawLog.Contains("Missing character:", StringComparison.Ordinal))
            return [];

        var seen = new Dictionary<string, DroppedGlyph>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in MissingCharRegex().Matches(Unwrap(rawLog)))
        {
            var cp = m.Groups["cp"].Value.ToUpperInvariant();
            if (seen.ContainsKey(cp)) continue;

            seen[cp] = new DroppedGlyph(
                Character: m.Groups["ch"].Value,
                CodePoint: cp,
                Font: m.Groups["font"].Success && m.Groups["font"].Value.Length > 0
                    ? m.Groups["font"].Value
                    : null);
        }

        return seen.Values.OrderBy(g => g.CodePoint, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// One warning line for the validation result, or null when nothing dropped.
    /// Phrased for someone who never chose a font — which is most Lilia authors,
    /// since <see cref="EngineDetector"/> picks the engine from commands present
    /// and prose contains none.
    /// </summary>
    public static string? Describe(IReadOnlyList<DroppedGlyph> dropped)
    {
        if (dropped.Count == 0) return null;

        var shown = string.Join(" ", dropped.Take(8).Select(g => g.Character));
        var more = dropped.Count > 8 ? $" (+{dropped.Count - 8} more)" : "";
        var font = dropped[0].Font is { Length: > 0 } f
            ? $" The font in use ({Truncate(f, 40)}) does not contain them."
            : "";

        return $"{dropped.Count} character{(dropped.Count == 1 ? " was" : "s were")} dropped from the "
             + $"output and will be missing from the PDF: {shown}{more}.{font}";
    }

    private static string Truncate(string s, int max)
    {
        // fontspec reports names as "name:Latin Modern Roman:mode=node;script=…"
        // — a lookup-kind prefix, the family, then a long OpenType feature
        // suffix. The family is the only part a human recognises, so drop the
        // prefix first and cut at the NEXT separator. Cutting at the first colon
        // yields the literal "name", which tells nobody anything.
        foreach (var prefix in (string[])["name:", "file:", "psname:"])
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..];
                break;
            }
        }

        var cut = s.IndexOfAny([':', ';']);
        if (cut > 0) s = s[..cut];

        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
