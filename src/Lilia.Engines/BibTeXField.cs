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

    private static readonly Dictionary<char, string> AccentMarks = new()
    {
        ['\''] = "\u0301", ['`'] = "\u0300", ['^'] = "\u0302", ['"'] = "\u0308", ['~'] = "\u0303",
        ['='] = "\u0304", ['.'] = "\u0307", ['u'] = "\u0306", ['v'] = "\u030C", ['H'] = "\u030B",
        ['c'] = "\u0327", ['k'] = "\u0328", ['r'] = "\u030A",
    };
    private static readonly Dictionary<string, string> Letters = new(StringComparer.Ordinal)
    {
        ["ss"] = "ß", ["o"] = "ø", ["O"] = "Ø", ["aa"] = "å", ["AA"] = "Å", ["ae"] = "æ", ["AE"] = "Æ",
        ["oe"] = "œ", ["OE"] = "Œ", ["l"] = "ł", ["L"] = "Ł", ["i"] = "ı", ["j"] = "ȷ", ["&"] = "&", ["%"] = "%",
        ["$"] = "$", ["#"] = "#", ["_"] = "_", ["textendash"] = "–", ["textemdash"] = "—",
    };
    private static readonly Dictionary<string, string> Greek = new(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε",
        ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["phi"] = "φ", ["varphi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ",
        ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["infty"] = "∞", ["ell"] = "ℓ", ["pm"] = "±", ["times"] = "×", ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥",
    };

    /// <summary>
    /// A BibTeX field as the text a reader sees, for Word and the preview (MCP-98: the Word
    /// bibliography printed <c>Fej{\'e}r</c>, <c>--</c>, <c>{NLP}</c>, <c>$\Delta$</c> as typed):
    /// accents become letters, -- and --- dashes, ~ a space, protecting braces go, and the Greek
    /// letters and few symbols a title holds in maths become characters.
    /// </summary>
    public static string ToPlain(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var s = value;
        // \'{e}, {\'e}, \'e, \c{c}, \"{\i}
        s = Regex.Replace(s, @"\\(?<m>['`^""~=.uvHckr])\s*(?:\{\s*(?:\\(?<dot>[ij])\b\s*|(?<ch>[A-Za-z]))\s*\}|\\(?<dot>[ij])\b|(?<ch>[A-Za-z]))",
            m =>
            {
                var letter = m.Groups["dot"].Success ? m.Groups["dot"].Value : m.Groups["ch"].Value;
                return AccentMarks.TryGetValue(m.Groups["m"].Value[0], out var mark)
                    ? (letter + mark).Normalize(NormalizationForm.FormC)
                    : letter;
            });
        // A control word eats the space after it; a control symbol (\&, \$) doesn't. An escaped $ is
        // held aside so the maths dollars can go.
        s = Regex.Replace(s, @"\\(?:(?<n>[A-Za-z]+)\b\s?(?:\{\})?|(?<n>[&%$#_]))", m =>
            m.Groups["n"].Value == "$" ? "\uE000"
            : Letters.TryGetValue(m.Groups["n"].Value, out var l) ? l
            : Greek.TryGetValue(m.Groups["n"].Value, out var g) ? g
            : m.Value);
        // Text commands keep their argument: \emph{x}, \textit{x}, \mathrm{x}.
        s = Regex.Replace(s, @"\\(?:emph|textit|textbf|textrm|textsc|texttt|mathrm|mathit|mathbf|mathcal|mathbb|url|href)\s*\{([^{}]*)\}", "$1");
        s = s.Replace("---", "—").Replace("--", "–").Replace('~', '\u00A0');
        s = s.Replace("$", "");
        s = Regex.Replace(s, @"[{}]", "");
        s = s.Replace("\\", "").Replace('\uE000', '$');
        return Regex.Replace(s, @"\s+", " ").Trim();
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
