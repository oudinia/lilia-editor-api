using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// A BibTeX field value as LaTeX, the way BibTeX copies it into the .bbl (MCP-90): <c>Fej{\'e}r</c>,
/// <c>$\Delta$</c> and <c>{NLP}</c> are already LaTeX, and the check's bibliography escaped them to
/// <c>Fej\{\textbackslash{}'e\}r</c>. Only what would break the compile is escaped: a bare
/// <c>&amp;</c>, <c>%</c>, <c>#</c>, and <c>_</c> or <c>^</c> outside maths; unbalanced braces fall
/// back to plain escaping.
/// </summary>
public static class BibTeXField
{
    public static string ToLatex(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!Balanced(value) || value.Count(c => c == '$') % 2 == 1) return LatexText.Escape(value);
        var sb = new StringBuilder(value.Length + 8);
        var math = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                sb.Append(c).Append(value[++i]);
                continue;
            }
            if (c == '$') math = !math;
            if (c is '&' or '%' or '#' || (!math && c is '_')) sb.Append('\\').Append(c);
            else if (!math && c == '^') sb.Append(@"\^{}");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>BibTeX's names, shortened as natbib's author-year label prints them: "Knuth",
    /// "Hardy and Wright", "Fejér et al.".</summary>
    public static string ShortAuthors(string? authors)
    {
        if (string.IsNullOrWhiteSpace(authors)) return "";
        var names = Regex.Split(authors.Trim(), @"\s+and\s+", RegexOptions.IgnoreCase)
            .Where(n => n.Length > 0 && !n.Equals("others", StringComparison.OrdinalIgnoreCase)).ToList();
        var more = Regex.IsMatch(authors, @"\band\s+others\s*$", RegexOptions.IgnoreCase);
        static string Last(string name) =>
            name.Contains(',') ? name[..name.IndexOf(',')].Trim() : name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        return names.Count switch
        {
            0 => "",
            1 when !more => Last(names[0]),
            2 when !more => $"{Last(names[0])} and {Last(names[1])}",
            _ => $"{Last(names[0])} et~al.",
        };
    }

    private static bool Balanced(string s)
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
