using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// A maths command Mathpix left in running text, outside <c>\(…\)</c> or <c>$…$</c>, set back in maths. The launch bot's
/// bulk import of handwritten notes (21 Nov 2026) printed a stray <c>\textbackslash{}mathrm</c>: when Mathpix reads a
/// formula as text, its LaTeX writes the command's characters as text (<c>\textbackslash mathrm\{d\}</c>), which the import
/// keeps as printed text (launch-bot #252). And a bare command in text was worse: the LaTeX import reads text, so
/// <c>\frac{1}{2}</c> became "1{2}", <c>\mathrm{d}</c> lost its maths, and <c>\alpha</c>, <c>\sum_{i}</c> or
/// <c>\mathrm d</c> stopped the PDF ("Missing $ inserted").
///
/// <para>Each such command, with its arguments and any sub- or superscript after it, becomes inline maths:
/// <c>\textbackslash mathrm\{d\} x</c> and <c>\mathrm{d}x</c> are both <c>$\mathrm{d}$x</c>. Only commands that exist in
/// maths alone are read (Greek letters, <c>\frac</c>, <c>\sum</c>, <c>\mathrm</c> …), never one text has too
/// (<c>\ldots</c>, <c>\textbackslash</c> itself before anything else). Maths, verbatim and comments are left as they are.
/// Used on Mathpix's output only (scan and PDF import, its tex.zip and its Markdown): a LaTeX source never has such text,
/// since it could not compile.</para>
/// </summary>
public static partial class OcrMathText
{
    /// <summary>Maths-only commands, by how many brace arguments each takes.</summary>
    private static readonly Dictionary<string, int> MathOnly = Build();

    private static Dictionary<string, int> Build()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in (
            "alpha beta gamma delta epsilon varepsilon zeta eta theta vartheta iota kappa lambda mu nu xi pi varpi rho varrho "
            + "sigma varsigma tau upsilon phi varphi chi psi omega Gamma Delta Theta Lambda Xi Pi Sigma Upsilon Phi Psi Omega "
            + "sum prod coprod int iint iiint oint lim limsup liminf sup inf max min det log ln exp sin cos tan cot sec csc "
            + "arcsin arccos arctan sinh cosh tanh arg deg dim gcd hom ker Pr "
            + "cdot cdots ddots vdots times div pm mp leq le geq ge neq ne approx equiv sim simeq cong propto ll gg "
            + "infty partial nabla to rightarrow leftarrow Rightarrow Leftarrow Leftrightarrow leftrightarrow longrightarrow "
            + "mapsto implies iff in notin ni subset subseteq supset supseteq cup cap setminus forall exists nexists emptyset "
            + "varnothing circ prime perp parallel angle hbar ell Re Im aleph wedge vee oplus otimes odot langle rangle "
            + "lfloor rfloor lceil rceil mid uparrow downarrow").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            d[name] = 0;
        foreach (var name in "mathrm mathbf mathit mathcal mathbb mathfrak mathsf mathtt mathscr boldsymbol operatorname vec hat bar tilde dot ddot widehat widetilde overline overrightarrow sqrt".Split(' '))
            d[name] = 1;
        foreach (var name in "frac dfrac tfrac binom".Split(' '))
            d[name] = 2;
        return d;
    }

    /// <summary>
    /// <paramref name="source"/> (a .tex, or Mathpix's Markdown) with every maths command in its text set as inline maths;
    /// <paramref name="wrapped"/> says how many.
    /// </summary>
    public static string MathCommandsAsMaths(string source, out int wrapped)
    {
        wrapped = 0;
        if (string.IsNullOrEmpty(source) || source.IndexOf('\\') < 0) return source;
        // The body only: the preamble's \newcommand definitions are not text.
        var begin = source.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var start = begin < 0 ? 0 : begin + @"\begin{document}".Length;
        var endAt = source.IndexOf(@"\end{document}", start, StringComparison.Ordinal);
        var end = endAt < 0 ? source.Length : endAt;
        var body = source[start..end];

        var count = 0;
        body = Outside(body, Unescape);
        body = Outside(body, text => Wrap(text, ref count));
        wrapped = count;
        return source[..start] + body + source[end..];
    }

    /// <summary>Applies <paramref name="change"/> to the text between maths, verbatim and comments.</summary>
    private static string Outside(string body, Func<string, string> change)
    {
        var kept = Protected(body);
        var sb = new StringBuilder(body.Length);
        var last = 0;
        foreach (var (s, e) in kept)
        {
            if (s < last) continue;
            sb.Append(change(body[last..s])).Append(body, s, e - s);
            last = e;
        }
        sb.Append(change(body[last..]));
        return sb.ToString();
    }

    private static List<(int Start, int End)> Protected(string body) =>
        OcrMathGuard.MathRegion().Matches(body).Select(m => (m.Index, m.Index + m.Length))
            .Concat(Verbatim().Matches(body).Select(m => (m.Index, m.Index + m.Length)))
            .Concat(Comment().Matches(body).Select(m => (m.Index, m.Index + m.Length)))
            .OrderBy(r => r.Item1)
            .ToList();

    /// <summary>
    /// <c>\textbackslash mathrm\{d\}</c> back to <c>\mathrm{d}</c>: a maths command's name after an escaped backslash, and its
    /// escaped argument groups and scripts (<c>\_</c>, <c>\^{}</c>).
    /// </summary>
    private static string Unescape(string text)
    {
        if (text.IndexOf(@"\textbackslash", StringComparison.Ordinal) < 0) return text;
        var named = EscapedCommand().Replace(text, m =>
            MathOnly.ContainsKey(m.Groups["name"].Value) ? @"\" + m.Groups["name"].Value : m.Value);
        return named == text ? text : UnescapeArguments(named);
    }

    /// <summary>After each maths command, its escaped groups <c>\{…\}</c> and scripts as real ones.</summary>
    private static string UnescapeArguments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        foreach (Match m in BareCommand().Matches(text))
        {
            if (m.Index < i || !MathOnly.ContainsKey(m.Groups["name"].Value)) continue;
            sb.Append(text, i, m.Index + m.Length - i);
            i = m.Index + m.Length;
            while (true)
            {
                var j = i;
                if (At(text, j, @"\_")) { sb.Append('_'); i = j + 2; continue; }
                if (At(text, j, @"\textasciicircum{}")) { sb.Append('^'); i = j + @"\textasciicircum{}".Length; continue; }
                if (At(text, j, @"\^{}")) { sb.Append('^'); i = j + 4; continue; }
                while (j < text.Length && text[j] == ' ') j++;
                if (!At(text, j, @"\{")) break;
                var close = text.IndexOf(@"\}", j + 2, StringComparison.Ordinal);
                if (close < 0) break;
                var inner = text[(j + 2)..close]
                    .Replace(@"\_", "_").Replace(@"\textasciicircum{}", "^").Replace(@"\^{}", "^");
                inner = EscapedCommand().Replace(inner, c => MathOnly.ContainsKey(c.Groups["name"].Value) ? @"\" + c.Groups["name"].Value : c.Value);
                sb.Append('{').Append(inner).Append('}');
                i = close + 2;
            }
        }
        sb.Append(text, i, text.Length - i);
        return sb.ToString();
    }

    private static bool At(string s, int i, string what) => string.CompareOrdinal(s, i, what, 0, what.Length) == 0;

    /// <summary>Every maths-only command in text, with its arguments and scripts, as <c>$…$</c>.</summary>
    private static string Wrap(string text, ref int count)
    {
        if (text.IndexOf('\\') < 0) return text;
        var sb = new StringBuilder(text.Length + 8);
        var i = 0;
        foreach (Match m in BareCommand().Matches(text))
        {
            if (m.Index < i) continue;
            var name = m.Groups["name"].Value;
            if (!MathOnly.TryGetValue(name, out var arity)) continue;
            var endAt = m.Index + m.Length;
            // \sqrt[3]{x}: its optional root.
            if (name == "sqrt" && endAt < text.Length && text[endAt] == '[')
            {
                var close = text.IndexOf(']', endAt);
                if (close > 0) endAt = close + 1;
            }
            for (var a = 0; a < arity; a++)
            {
                var j = endAt;
                while (j < text.Length && text[j] is ' ' or '\t') j++;
                var next = Token(text, j);
                if (next < 0) break;
                endAt = next;
            }
            // Sub- and superscripts right after it: \sum_{i=1}^{n}, \alpha_1.
            while (endAt < text.Length && text[endAt] is '_' or '^')
            {
                var next = Token(text, endAt + 1);
                if (next < 0) break;
                endAt = next;
            }
            sb.Append(text, i, m.Index - i).Append('$').Append(text, m.Index, endAt - m.Index).Append('$');
            count++;
            i = endAt;
        }
        sb.Append(text, i, text.Length - i);
        return sb.ToString();
    }

    /// <summary>The end of the token at <paramref name="at"/>: a balanced group, a command, or one character; -1 when none.</summary>
    private static int Token(string s, int at)
    {
        if (at >= s.Length || char.IsWhiteSpace(s[at]) || s[at] is '}' or '$') return -1;
        if (s[at] == '{')
        {
            var depth = 0;
            for (var k = at; k < s.Length; k++)
            {
                if (s[k] == '\\') { k++; continue; }
                if (s[k] == '{') depth++;
                else if (s[k] == '}' && --depth == 0) return k + 1;
            }
            return -1;
        }
        if (s[at] == '\\')
        {
            var k = at + 1;
            while (k < s.Length && char.IsAsciiLetter(s[k])) k++;
            return k == at + 1 ? Math.Min(at + 2, s.Length) : k;
        }
        return at + 1;
    }

    /// <summary>A command whose backslash is not itself escaped.</summary>
    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)\\(?<name>[A-Za-z]+)")]
    private static partial Regex BareCommand();

    /// <summary>\textbackslash, \textbackslash{} or \textbackslash{ }, then a command name.</summary>
    [GeneratedRegex(@"\\textbackslash(?:\s*\{\s*\})?\s*(?<name>[A-Za-z]+)")]
    private static partial Regex EscapedCommand();

    [GeneratedRegex(@"\\begin\{(?<env>verbatim|lstlisting|minted|comment|Verbatim)\*?\}.*?\\end\{\k<env>\*?\}", RegexOptions.Singleline)]
    private static partial Regex Verbatim();

    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)%[^\n]*")]
    private static partial Regex Comment();
}
