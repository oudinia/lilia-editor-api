using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// The author string as it goes into <c>\author{}</c>.
///
/// <para>The Title block keeps the author as LaTeX source (<c>Jane\thanks{…}\\U. of South \and John</c>),
/// and the authors editor promises that what the fields cannot show, <c>\orcidlink</c>, <c>\href</c>,
/// <c>\textit</c>, <c>\IEEEauthorblockN</c>, is "exported exactly as written". The exporters escaped
/// everything except <c>\and</c>, <c>\\</c>, <c>\today</c> and a <c>\thanks</c> without nested braces, so
/// those printed as literal text (5 Oct review). The user's decision: pass the commands through.</para>
///
/// <para>So commands, braces, <c>\\</c>, <c>~</c> and <c>$…$</c> pass as written. What a person types in a
/// plain field still has to print: a bare <c>&amp; % # _</c> is escaped (<c>R&amp;D Lab</c>,
/// <c>jane_doe@x.org</c>, <c>50%</c>). A string whose braces do not balance falls back to escaping
/// everything, so a typo cannot break the document. What a command may do is the compile guard's
/// business (TexSourceGuard), which reads the whole source.</para>
/// </summary>
public static class AuthorLatex
{
    private static readonly Regex BareSpecial = new(@"(?<!\\)([&%#_])", RegexOptions.Compiled);

    public static string For(string? text, Func<string, string> escapeEverything)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.Trim();
        if (!Balanced(s)) return escapeEverything(s);
        return BareSpecial.Replace(s, @"\$1");
    }

    /// <summary>Braces pair up, ignoring escaped ones.</summary>
    public static bool Balanced(string s)
    {
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }
}
