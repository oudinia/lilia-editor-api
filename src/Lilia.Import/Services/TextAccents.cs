using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// TeX's text accents (<c>\r{a}</c>, <c>\'e</c>, <c>\v s</c>, <c>\t{oo}</c> …) and <c>\char</c> (<c>\char64</c>,
/// <c>\char"41</c>, <c>\char'101</c>, <c>\char`\{</c>) as the characters they print.
/// <para>18 Nov 2026, launch-bot #169: <c>Ume\r{a}</c> imported as "Ume" in an amsart \thanks and "Umea" in the body, and
/// <c>{\tt \char`\{ann, bob\char`\}@example.org}</c> lost "{ann" and both braces (its backticks were read as a code span,
/// and the body no longer compiled).</para>
/// </summary>
internal static class TextAccents
{
    // \'e  \'{e}  \"{\i}  \r{a}  \r a  \t{oo}. A one-letter command (\r, \v …) needs a space or a brace after it, so \renewcommand
    // or \bar is not one.
    private static readonly Regex Accent = new(
        @"(?<!\\)\\(?:(?<m>['`^""~=.])\s*|(?<m>[uvHckdbtr])(?:\s+|(?=\{)))"
        + @"(?:\{\s*(?:\\(?<dot>[ij])(?![A-Za-z])\s*|(?<ch>\p{L}{1,2}))\s*\}|\\(?<dot>[ij])(?![A-Za-z])|(?<ch>[A-Za-z]))",
        RegexOptions.Compiled);

    // \char64, \char"41, \char'101, \char`\{, \char`a; TeX takes one space after the number with it.
    private static readonly Regex Char = new(
        @"(?<!\\)\\char\s*(?:(?<dec>\d+)|""(?<hex>[0-9A-Fa-f]+)|'(?<oct>[0-7]+)|`\\?(?<chr>[^\s]))\s?", RegexOptions.Compiled);

    private static readonly Dictionary<char, char> Marks = new()
    {
        ['\''] = '́', ['`'] = '̀', ['^'] = '̂', ['"'] = '̈', ['~'] = '̃', ['='] = '̄',
        ['.'] = '̇', ['u'] = '̆', ['v'] = '̌', ['H'] = '̋', ['c'] = '̧', ['k'] = '̨',
        ['r'] = '̊', ['d'] = '̣', ['b'] = '̱', ['t'] = '͡',
    };

    // The accented letters pdflatex prints from UTF-8 with T1 (t1enc.dfu): Latin-1, Latin Extended-A and these. Any other
    // (\d{o}, \b{o}, \t{oo}) stays a command in the body, where it compiles.
    private static readonly HashSet<char> PdflatexExtra =
    [
        'Ǎ', 'ǎ', 'Ǐ', 'ǐ', 'Ǒ', 'ǒ', 'Ǔ', 'ǔ', 'Ǧ', 'ǧ', 'Ǩ', 'ǩ',
        'Ǫ', 'ǫ', 'ǰ', 'Ǵ', 'ǵ', 'Ȳ', 'ȳ', 'Ḃ', 'ḃ', 'ḍ', 'Ḟ', 'ḟ',
        'Ḡ', 'ḡ', 'ḥ', 'Ḱ', 'ḱ', 'ḷ', 'ṃ', 'ṅ', 'ṇ', 'ṛ', 'ṣ', 'ṭ',
        'Ẏ', 'ẏ', 'Ẑ', 'ẑ', 'Ỳ', 'ỳ',
    ];

    /// <summary>
    /// <paramref name="text"/> with its accents and <c>\char</c>s as characters. A brace from <c>\char</c> comes back escaped
    /// (<c>\{</c>), as do <c>&amp; % # _ $</c>. With <paramref name="keep"/> (the body), an accent pdflatex can't print as a
    /// character, or a <c>\char</c> that isn't a plain one, goes through it as LaTeX to set aside; without it (the title
    /// block's author text, read by TitleAuthors), every accent is a character.
    /// </summary>
    public static string Resolve(string text, Func<string, string>? keep = null)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0) return text;
        text = Char.Replace(text, m =>
        {
            int code;
            if (m.Groups["chr"].Success) code = m.Groups["chr"].Value[0];
            else if (m.Groups["hex"].Success) code = Convert.ToInt32(m.Groups["hex"].Value, 16);
            else if (m.Groups["oct"].Success) code = Convert.ToInt32(m.Groups["oct"].Value, 8);
            else if (!int.TryParse(m.Groups["dec"].Value, out code)) return m.Value;
            return code switch
            {
                '{' or '}' or '&' or '%' or '#' or '_' or '$' => "\\" + (char)code,
                >= 32 and <= 126 and not ('\\' or '^' or '~' or '`') => ((char)code).ToString(),
                _ when keep is not null => keep($@"\char{code} "),
                >= 32 and <= 126 => ((char)code).ToString(),
                _ => m.Value,
            };
        });
        // Innermost first: \~{\^e} is ê, then ễ.
        for (var pass = 0; pass < 3; pass++)
        {
            var before = text;
            text = Accent.Replace(text, m =>
            {
                var mark = m.Groups["m"].Value[0];
                var letters = m.Groups["dot"].Success ? m.Groups["dot"].Value : m.Groups["ch"].Value;
                if (letters.Length == 2 && mark != 't') return m.Value;
                if (mark == 't' && letters.Length != 2) return m.Value;
                var written = m.Groups["dot"].Success ? @"\" + letters : letters;
                var composed = mark == 't'
                    ? letters[0] + "͡" + letters[1]
                    : (letters + Marks[mark]).Normalize(NormalizationForm.FormC);
                var printable = composed.Length == 1 && (composed[0] is >= 'À' and <= 'ſ' || PdflatexExtra.Contains(composed[0]));
                return printable || keep is null ? composed : keep($@"\{mark}{{{written}}}");
            });
            if (text == before) break;
        }
        return text;
    }
}
