using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// Maths that Mathpix left in running text, set back in maths, and Mathpix's maths made whole. Used on Mathpix's output
/// only (scan and PDF import, its tex.zip and its Markdown), before the LaTeX is read: a LaTeX source never has such text,
/// since it could not compile.
///
/// <para>The launch bot's bulk import of handwritten notes (21 Nov 2026). When Mathpix reads a formula as text, its LaTeX
/// writes the formula's characters as text: <c>\textbackslash mathrm\{d\}</c>, <c>\textbackslash left(</c> …
/// <c>\textbackslash right)</c>, <c>\textbackslash qquad</c>, <c>x\_\{i\}</c>. Kept as printed text, or read as commands
/// out of maths, they printed <c>\textbackslash{}right</c> and stopped the PDF: "\mathrm allowed only in math mode",
/// "Missing $ inserted", "Extra }, or forgotten $" (API #677 handled a list of commands with flat arguments; the retest
/// of the same notebooks still failed on <c>\right</c>, <c>\qquad</c>, nested arguments, <c>\limits</c> and a
/// <c>$$</c> left open).</para>
///
/// <para>Three passes over the document's body (never its preamble, verbatim or comments):
/// <list type="number">
/// <item><b>Delimiters balanced</b> (<see cref="BalanceDelimiters"/>): maths opened with <c>$</c>, <c>$$</c>, <c>\(</c> or
///   <c>\[</c> is closed where TeX would stop at the latest: a blank line, the end of the group it opened in, the end of
///   the body. A display closed by a single <c>$</c> is closed by <c>$$</c>; a stray closer is dropped.</item>
/// <item><b>Each formula made whole</b> (<see cref="MendMath"/>): an escaped command in it is a command again,
///   <c>\limits</c> or <c>\nolimits</c> that follows no operator is dropped ("Limit controls must follow a math
///   operator"), and its braces and <c>\left</c>/<c>\right</c> pairs are balanced.</item>
/// <item><b>Maths in the text set as maths</b> (<see cref="MathRuns"/>): from a maths-only command, escaped or not, the
///   run of maths that follows (its arguments, nested or escaped, scripts, symbols, single letters, a whole
///   <c>\left … \right</c> pair, and words only inside a group or a pair) is unescaped, mended and set as inline maths.
///   <c>\textbackslash mathrm\{d\} x</c>, <c>\mathrm{d}x</c> and <c>\mathrm d x</c> are <c>$\mathrm{d} x$</c>.</item>
/// </list></para>
/// </summary>
public static partial class OcrMathText
{
    /// <summary>Commands that exist in maths only: one of them in text starts a run of maths.</summary>
    private static readonly HashSet<string> MathOnly = new((
        "alpha beta gamma delta epsilon varepsilon zeta eta theta vartheta iota kappa varkappa lambda mu nu xi pi varpi rho "
        + "varrho sigma varsigma tau upsilon phi varphi chi psi omega digamma Gamma Delta Theta Lambda Xi Pi Sigma Upsilon "
        + "Phi Psi Omega varGamma varDelta varTheta varLambda varXi varPi varSigma varUpsilon varPhi varPsi varOmega "
        + "sum prod coprod int iint iiint oint lim limsup liminf max min sup inf det log ln lg exp sin cos tan cot sec csc "
        + "arcsin arccos arctan sinh cosh tanh coth arg deg dim gcd hom ker Pr bigcup bigcap bigoplus bigotimes bigodot "
        + "biguplus bigvee bigwedge bigsqcup smallint "
        + "cdot cdots ddots vdots times div pm mp leq le geq ge leqslant geqslant neq ne approx equiv sim simeq cong propto "
        + "asymp doteq lesssim gtrsim ll gg prec succ preceq succeq "
        + "infty partial nabla to rightarrow leftarrow Rightarrow Leftarrow Leftrightarrow leftrightarrow longrightarrow "
        + "longleftarrow Longrightarrow Longleftarrow longleftrightarrow Longleftrightarrow mapsto longmapsto hookrightarrow "
        + "uparrow downarrow Uparrow Downarrow nearrow searrow rightleftharpoons implies iff "
        + "in notin ni subset subseteq supset supseteq subsetneq supsetneq cup cap setminus sqcup sqcap uplus forall exists "
        + "nexists emptyset varnothing neg lnot land lor wedge vee top bot vdash models circ bullet star ast prime perp "
        + "parallel angle triangle hbar ell Re Im wp aleph imath jmath oplus ominus otimes oslash odot langle rangle lfloor "
        + "rfloor lceil rceil lvert rvert lVert rVert mid nmid therefore because square Box blacksquare "
        + "mathrm mathbf mathit mathcal mathbb mathfrak mathsf mathtt mathscr boldsymbol bm operatorname mathop mathbin "
        + "mathrel vec hat bar tilde dot ddot acute grave breve check mathring widehat widetilde overline underline overrightarrow "
        + "overleftarrow underbrace overbrace underset overset stackrel xrightarrow xleftarrow sqrt frac dfrac tfrac cfrac "
        + "binom dbinom tbinom pmod bmod "
        + "left right bigl bigr Bigl Bigr biggl biggr Biggl Biggr big Big bigg Bigg middle limits nolimits displaystyle "
        + "textstyle scriptstyle").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>Operators \limits and \nolimits may follow.</summary>
    private static readonly HashSet<string> Operators = new((
        "sum prod coprod int iint iiint oint lim limsup liminf max min sup inf det gcd Pr bigcup bigcap bigoplus bigotimes "
        + "bigodot biguplus bigvee bigwedge bigsqcup smallint mathop operatorname").Split(' '), StringComparer.Ordinal);

    /// <summary>Commands text has too: never the start of a run, but part of one, escaped or not.</summary>
    private static readonly HashSet<string> Neutral = new("quad qquad ldots dots".Split(' '), StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="source"/> (a .tex, or Mathpix's Markdown) with its maths balanced and mended, and every run of maths
    /// in its text set as inline maths; <paramref name="wrapped"/> says how many runs.
    /// </summary>
    public static string MathCommandsAsMaths(string source, out int wrapped)
    {
        wrapped = 0;
        if (string.IsNullOrEmpty(source)) return source;
        // The body only: the preamble's \newcommand definitions are not text.
        var begin = source.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var start = begin < 0 ? 0 : begin + @"\begin{document}".Length;
        var endAt = source.IndexOf(@"\end{document}", start, StringComparison.Ordinal);
        var end = endAt < 0 ? source.Length : endAt;
        var body = source[start..end];
        if (body.IndexOf('\\') < 0 && body.IndexOf('$') < 0) return source;

        body = BalanceDelimiters(body);
        var count = 0;
        var sb = new StringBuilder(body.Length + 16);
        var pieces = Pieces(body).ToList();
        for (var p = 0; p < pieces.Count; p++)
        {
            var (text, isMath) = pieces[p];
            sb.Append(isMath ? MendRegion(text) : MathRuns(text, ref count,
                afterMath: p > 0 && pieces[p - 1].IsMath, beforeMath: p + 1 < pieces.Count && pieces[p + 1].IsMath));
        }
        wrapped = count;
        return source[..start] + Join(sb.ToString()) + source[end..];
    }

    /// <summary>Where a run of maths touches other maths.</summary>
    private const char Junction = '\uE0A0';

    /// <summary>
    /// Two inline formulas that touch become one (<c>$x$</c> and <c>$\alpha$</c> are <c>$x\alpha$</c>); next to a display
    /// or a <c>\(…\)</c>, a space keeps them apart.
    /// </summary>
    private static string Join(string body)
    {
        if (body.IndexOf(Junction) < 0) return body;
        body = Regex.Replace(body, "(?<![$\\\\])\\$" + Junction + "\\$(?!\\$)", "");
        return body.Replace(Junction, ' ');
    }

    // ── Pieces: text, and the maths, verbatim and comments that are not text ───────────────────────────────────────

    /// <summary>The body cut into text and the rest (maths regions are flagged so they can be mended).</summary>
    private static IEnumerable<(string Text, bool IsMath)> Pieces(string body)
    {
        var kept = Kept(body).Concat(OcrMathGuard.MathRegion().Matches(body).Select(m => (m.Index, m.Index + m.Length, true)))
            .OrderBy(r => r.Item1).ToList();
        var last = 0;
        foreach (var (s, e, isMath) in kept)
        {
            if (s < last) continue;
            if (s > last) yield return (body[last..s], false);
            yield return (body[s..e], isMath);
            last = e;
        }
        if (last < body.Length) yield return (body[last..], false);
    }

    /// <summary>Verbatim environments and comments: never touched.</summary>
    private static IEnumerable<(int, int, bool)> Kept(string body) =>
        Verbatim().Matches(body).Select(m => (m.Index, m.Index + m.Length, false))
            .Concat(Comment().Matches(body).Select(m => (m.Index, m.Index + m.Length, false)));

    // ── 1. Delimiters ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every <c>$</c>, <c>$$</c>, <c>\(</c> and <c>\[</c> closed where TeX would stop: before a blank line, before the
    /// <c>}</c> of the group it opened in, at the end; a display closed by one <c>$</c> gets <c>$$</c>; a closer with
    /// nothing open is dropped. Maths environments, verbatim and comments are skipped whole.
    /// </summary>
    internal static string BalanceDelimiters(string body)
    {
        var skip = Kept(body).Select(k => (Start: k.Item1, End: k.Item2, Env: false))
            .Concat(MathEnvironment().Matches(body).Select(m => (Start: m.Index, End: m.Index + m.Length, Env: true)))
            .OrderBy(r => r.Start).ToList();
        var sb = new StringBuilder(body.Length + 8);
        string? open = null;          // "$", "$$", "\(", "\["
        var depth = 0;                // brace depth in the text
        var openDepth = 0;            // the depth the maths opened at
        var k = 0;
        string Closer(string o) => o switch { "$" => "$", "$$" => "$$", @"\(" => @"\)", _ => @"\]" };
        for (var i = 0; i < body.Length;)
        {
            while (k < skip.Count && skip[k].End <= i) k++;
            if (k < skip.Count && skip[k].Start <= i)
            {
                // A maths environment inside open maths can't be: the maths is closed first.
                if (open is not null && skip[k].Env) { sb.Append(Closer(open)); open = null; }
                sb.Append(body, i, skip[k].End - i);
                i = skip[k].End;
                continue;
            }
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                var n = body[i + 1];
                if (n is '(' or '[')
                {
                    if (open is null) { open = n == '(' ? @"\(" : @"\["; openDepth = depth; sb.Append(c).Append(n); }
                    else sb.Append(c).Append(n);             // \( inside maths: left to the formula
                    i += 2;
                    continue;
                }
                if (n is ')' or ']')
                {
                    var closer = n == ')' ? @"\)" : @"\]";
                    if (open is not null && Closer(open) == closer) { sb.Append(closer); open = null; }
                    else if (open is not null) { sb.Append(Closer(open)); open = null; }   // the wrong closer: the right one
                    // nothing open: a stray closer is dropped
                    i += 2;
                    continue;
                }
                // An escaped character (\$ \{ \} \\ …) is one token.
                sb.Append(c).Append(n);
                i += 2;
                continue;
            }
            if (c == '$')
            {
                var dbl = i + 1 < body.Length && body[i + 1] == '$';
                if (open is null)
                {
                    open = dbl ? "$$" : "$";
                    openDepth = depth;
                    sb.Append(open);
                    i += dbl ? 2 : 1;
                    continue;
                }
                if (open == "$$")
                {
                    // A display ends with $$, even when Mathpix wrote one $ ("Display math should end with $$").
                    sb.Append("$$");
                    open = null;
                    i += dbl ? 2 : 1;
                    continue;
                }
                if (open == "$")
                {
                    sb.Append('$');
                    open = null;
                    i += 1;   // a second $ right after opens new maths: read on
                    continue;
                }
                // $ inside \( or \[: a literal dollar would stop it; drop it.
                i += dbl ? 2 : 1;
                continue;
            }
            if (c == '{') depth++;
            if (c == '}')
            {
                if (open is not null && depth <= openDepth) { sb.Append(Closer(open)); open = null; }
                depth--;
            }
            if (c == '\n' && open is not null && BlankLineAt(body, i)) { sb.Append(Closer(open)); open = null; }
            sb.Append(c);
            i++;
        }
        if (open is not null) sb.Append(Closer(open));
        return sb.ToString();
    }

    private static bool BlankLineAt(string s, int newline)
    {
        for (var j = newline + 1; j < s.Length; j++)
        {
            if (s[j] == '\n') return true;
            if (s[j] is not (' ' or '\t' or '\r')) return false;
        }
        return true;
    }

    // ── 2. A formula ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A maths region as found (its delimiters kept), its body mended.</summary>
    private static string MendRegion(string region)
    {
        var m = OcrMathGuard.MathRegion().Match(region);
        if (!m.Success || m.Index != 0 || m.Length != region.Length) return region;
        var body = m.Groups["body"];
        return region[..body.Index] + MendMath(body.Value) + region[(body.Index + body.Length)..];
    }

    /// <summary>
    /// A formula's LaTeX made whole: escaped commands and characters unescaped, <c>\limits</c> after no operator dropped,
    /// braces and <c>\left</c>/<c>\right</c> balanced.
    /// </summary>
    internal static string MendMath(string math)
    {
        if (string.IsNullOrEmpty(math)) return math;
        if (math.Contains(@"\text", StringComparison.Ordinal)) math = UnescapeInMath(math);
        math = DropStrayLimits(math);
        return Balance(math);
    }

    private static string UnescapeInMath(string math)
    {
        // \textbackslash is a text command: in maths it is an escaped command or symbol.
        math = EscapedBackslash().Replace(math, m => @"\" + m.Groups["what"].Value);
        return math.Replace(@"\textasciicircum{}", "^").Replace(@"\textasciicircum", "^")
            .Replace(@"\textunderscore{}", "_").Replace(@"\textunderscore", "_")
            .Replace(@"\textasciitilde{}", "~");
    }

    /// <summary>\limits or \nolimits right after an operator (and its scripts) stays; anywhere else it stops TeX.</summary>
    private static string DropStrayLimits(string math)
    {
        if (math.IndexOf("limits", StringComparison.Ordinal) < 0) return math;
        return Limits().Replace(math, m =>
        {
            // What precedes it, its spaces and a group (\mathop{…}, \operatorname*{…}) skipped.
            var before = math[..m.Index].TrimEnd();
            if (before.EndsWith('}'))
            {
                var open = OpeningBrace(before, before.Length - 1);
                if (open > 0) before = before[..open].TrimEnd().TrimEnd('*');
            }
            var name = TrailingCommand().Match(before);
            return name.Success && Operators.Contains(name.Groups["name"].Value) ? m.Value : "";
        });
    }

    /// <summary>Unmatched <c>}</c> dropped, missing ones added, and every <c>\left</c> given its <c>\right</c>.</summary>
    internal static string Balance(string math)
    {
        var sb = new StringBuilder(math.Length + 8);
        var depth = 0;
        for (var i = 0; i < math.Length; i++)
        {
            var c = math[i];
            if (c == '\\' && i + 1 < math.Length) { sb.Append(c).Append(math[i + 1]); i++; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                if (depth == 0) continue;
                depth--;
            }
            sb.Append(c);
        }
        if (depth > 0) sb.Append('}', depth);
        var s = sb.ToString();
        var lefts = LeftCommand().Matches(s).Count;
        var rights = RightCommand().Matches(s).Count;
        if (lefts > rights) s += string.Concat(Enumerable.Repeat(@" \right.", lefts - rights));
        else if (rights > lefts) s = string.Concat(Enumerable.Repeat(@"\left. ", rights - lefts)) + s;
        return s;
    }

    private static int OpeningBrace(string s, int close)
    {
        var depth = 0;
        for (var i = close; i >= 0; i--)
        {
            if (i > 0 && s[i - 1] == '\\') continue;
            if (s[i] == '}') depth++;
            else if (s[i] == '{' && --depth == 0) return i;
        }
        return -1;
    }

    // ── 3. Maths in text ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One token of maths read from text: what it is, how it is written in maths, and how many characters it took.</summary>
    private enum Kind { Command, Neutral, Open, Close, Script, Symbol, Letter, Word, Space, Stop }

    private readonly record struct Token(Kind Kind, int Length, string Maths, string? Name = null);

    /// <summary>How many arguments a command takes, for one written without braces (<c>\mathrm d</c>, <c>\frac12</c>).</summary>
    private static int Arity(string? name) => name switch
    {
        "frac" or "dfrac" or "tfrac" or "cfrac" or "binom" or "dbinom" or "tbinom" or "underset" or "overset" or "stackrel" => 2,
        "mathrm" or "mathbf" or "mathit" or "mathcal" or "mathbb" or "mathfrak" or "mathsf" or "mathtt" or "mathscr" or "boldsymbol"
            or "bm" or "operatorname" or "mathop" or "mathbin" or "mathrel" or "vec" or "hat" or "bar" or "tilde" or "dot" or "ddot"
            or "acute" or "grave" or "breve" or "check" or "mathring" or "widehat" or "widetilde" or "overline" or "underline"
            or "overrightarrow" or "overleftarrow" or "underbrace" or "overbrace" or "sqrt" or "xrightarrow" or "xleftarrow"
            or "pmod" => 1,
        _ => 0,
    };

    private static readonly HashSet<string> Sized = new(
        "left right middle bigl bigr Bigl Bigr biggl biggr Biggl Biggr big Big bigg Bigg".Split(' '), StringComparer.Ordinal);

    /// <summary>The text with every run of maths in it set as inline maths.</summary>
    private static string MathRuns(string text, ref int count, bool afterMath = false, bool beforeMath = false)
    {
        if (text.IndexOf('\\') < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        var i = 0;
        while (i < text.Length)
        {
            var t = Read(text, i);
            if (t.Kind == Kind.Command && MathOnly.Contains(t.Name!) && Run(text, i) is { } run)
            {
                // Next to other maths ($x$\alpha), a {} keeps the two apart: $x$$\alpha$ would read as a display.
                // Next to other maths ($x$\alpha): a mark, where the two become one formula (Join). $x$$\alpha$ would pair
                // the wrong dollars in Lilia's text, and the LaTeX import drops a {} between them.
                if (sb.Length > 0 ? sb[^1] == '$' : afterMath) sb.Append(Junction);
                sb.Append('$').Append(run.Maths).Append('$');
                if (run.End < text.Length ? text[run.End] == '$' : beforeMath) sb.Append(Junction);
                count++;
                i = run.End;
                continue;
            }
            if (t.Kind == Kind.Neutral && text.AsSpan(i).StartsWith(@"\textbackslash"))
            {
                // An escaped command text has too: \textbackslash qquad is \qquad, \textbackslash , a thin space.
                sb.Append(t.Name is { } n && char.IsAsciiLetter(n[0]) ? t.Maths : t.Name == "," ? @"\, " : " ");
                i += t.Length;
                continue;
            }
            var step = t.Length > 0 ? t.Length : 1;
            sb.Append(text, i, step);
            i += step;
        }
        return sb.ToString();
    }

    /// <summary>The run of maths starting at <paramref name="start"/> (a maths-only command): its maths, mended, and its end.</summary>
    private static (string Maths, int End)? Run(string text, int start)
    {
        var parts = new List<(Token T, int At)>();
        var depth = 0;          // braces
        var pairs = 0;          // \left … \right
        var needArg = 0;        // arguments a command still takes
        var i = start;
        while (i < text.Length)
        {
            var t = Read(text, i);
            // Text inside a formula's group (\text{…}, \mbox{…}) is the formula's.
            if (t.Kind == Kind.Stop && depth > 0 && t.Name is "text" or "textrm" or "textit" or "textbf" or "mbox")
                t = t with { Kind = Kind.Command };
            if (t.Kind == Kind.Word && needArg > 0 && depth == 0)
                t = new Token(Kind.Letter, 1, text[i].ToString());   // \mathrm dx: its argument is d
            var stop = t.Kind switch
            {
                Kind.Stop => true,
                Kind.Close => depth == 0,
                Kind.Word => depth == 0 && pairs == 0,
                Kind.Space => depth == 0 && pairs == 0 && needArg == 0 && SpaceEnds(text, i + t.Length),
                _ => false,
            };
            if (t.Kind == Kind.Space && t.Maths.Length == 0) stop = true;   // a blank line
            if (stop) break;
            switch (t.Kind)
            {
                case Kind.Open: depth++; break;
                case Kind.Close: depth--; break;
                // A \left counts as a pair only when its \right comes later in the paragraph: then words up to it are the
                // formula's. A lone one gets \right. at the run's end (Balance) and words end the run as usual.
                case Kind.Command when t.Name == "left" && RightLater(text, i + t.Length): pairs++; break;
                case Kind.Command when t.Name == "right" && pairs > 0: pairs--; break;
            }
            if (depth == 0 || t.Kind == Kind.Open && depth == 1)
            {
                if (t.Kind == Kind.Command && Arity(t.Name) > 0) needArg = Arity(t.Name);
                else if (needArg > 0 && t.Kind is Kind.Open or Kind.Letter or Kind.Symbol or Kind.Command or Kind.Neutral) needArg--;
            }
            parts.Add((t, i));
            i += t.Length;
            // \left, \right and the sized delimiters take their delimiter with them: ( [ | . \{ …
            if (t.Kind == Kind.Command && Sized.Contains(t.Name!) && Delimiter(text, i) is { } d)
            {
                parts.Add((d, i));
                i += d.Length;
            }
        }
        // Trailing spaces and sentence punctuation are text.
        while (parts.Count > 0 && depth == 0 && pairs == 0 && needArg == 0
               && (parts[^1].T.Kind == Kind.Space || parts[^1].T.Kind == Kind.Symbol && parts[^1].T.Maths is "." or "," or ";" or ":" or "(" or "[" or "?" or "!"))
            parts.RemoveAt(parts.Count - 1);
        if (parts.Count == 0) return null;
        var end = parts[^1].At + parts[^1].T.Length;
        var maths = string.Concat(parts.Select(p => p.T.Maths)).TrimEnd();
        // A command left without its argument (\mathrm at the end of a line) gets an empty one.
        for (var n = 0; n < needArg; n++) maths += "{}";
        return (MendMath(maths), end);
    }

    /// <summary>Whether a \right, escaped or not, comes before the paragraph ends.</summary>
    private static bool RightLater(string text, int from)
    {
        var blank = BlankLine().Match(text, from);
        var end = blank.Success ? blank.Index : text.Length;
        return RightCommand().Match(text[from..end]).Success || text.IndexOf(@"\textbackslash right", from, end - from, StringComparison.Ordinal) >= 0
            || text.IndexOf(@"\textbackslash{}right", from, end - from, StringComparison.Ordinal) >= 0;
    }

    [GeneratedRegex(@"\n[ \t\r]*\n")]
    private static partial Regex BlankLine();

    /// <summary>
    /// After a space, the run goes on when what follows is still maths (a symbol, one letter, a script, a group, a
    /// command); a word, the end of the group or anything else is text again.
    /// </summary>
    private static bool SpaceEnds(string text, int at)
    {
        var next = Read(text, at);
        return next.Kind is Kind.Word or Kind.Stop or Kind.Close or Kind.Space;
    }

    /// <summary>The delimiter after \left, \right or \big…, or null.</summary>
    private static Token? Delimiter(string text, int i)
    {
        var from = i;
        while (i < text.Length && text[i] == ' ') i++;
        if (i >= text.Length) return null;
        var skipped = i - from;
        var c = text[i];
        if (c is '(' or ')' or '[' or ']' or '|' or '.' or '/' or '<' or '>') return new Token(Kind.Symbol, skipped + 1, c.ToString());
        foreach (var (written, maths) in EscapedDelimiters)
            if (text.AsSpan(i).StartsWith(written)) return new Token(Kind.Symbol, skipped + written.Length, maths);
        var cmd = BareName().Match(text, i);
        if (cmd.Success && cmd.Groups["name"].Value is "langle" or "rangle" or "lfloor" or "rfloor" or "lceil" or "rceil" or "vert"
                or "Vert" or "lvert" or "rvert" or "lVert" or "rVert" or "uparrow" or "downarrow" or "backslash")
            return new Token(Kind.Symbol, skipped + cmd.Length, @"\" + cmd.Groups["name"].Value + " ");
        var esc = EscapedBackslashAt().Match(text, i);
        if (esc.Success && esc.Groups["what"].Value is "langle" or "rangle" or "lfloor" or "rfloor" or "lceil" or "rceil" or "vert" or "Vert")
            return new Token(Kind.Symbol, skipped + esc.Length, @"\" + esc.Groups["what"].Value + " ");
        return null;
    }

    /// <summary>A delimiter as Mathpix escapes it in text, and as maths.</summary>
    private static readonly (string Written, string Maths)[] EscapedDelimiters =
    [
        (@"\textbackslash{}\{", @"\{"), (@"\textbackslash{}\}", @"\}"), (@"\textbackslash \{", @"\{"), (@"\textbackslash \}", @"\}"),
        (@"\textbackslash\{", @"\{"), (@"\textbackslash\}", @"\}"), (@"\textbackslash{}|", @"\|"), (@"\textbackslash |", @"\|"),
        (@"\{", @"\{"), (@"\}", @"\}"), (@"\textbar{}", "|"), (@"\textbar", "|"), (@"\textless{}", "<"), (@"\textgreater{}", ">"),
    ];

    /// <summary>Commands that end a run where they stand: text's own structure.</summary>
    private static bool TextStructure(string n) =>
        n.StartsWith("text", StringComparison.Ordinal) && n != "textstyle"
        || n is "section" or "subsection" or "subsubsection" or "paragraph" or "item" or "begin" or "end" or "emph" or "cite"
            or "ref" or "eqref" or "label" or "footnote" or "par" or "newline" or "url" or "href" or "includegraphics" or "caption"
            or "hline" or "noindent" or "centering" or "maketitle";

    /// <summary>The token at <paramref name="i"/>, read as maths written in text (escaped or not).</summary>
    private static Token Read(string s, int i)
    {
        if (i >= s.Length) return new Token(Kind.Stop, 0, "");
        var c = s[i];
        if (c == '\\')
        {
            // \textbackslash, its {} and its spaces, then what it escapes.
            var esc = EscapedBackslashAt().Match(s, i);
            if (esc.Success)
            {
                var what = esc.Groups["what"].Value;
                if (char.IsAsciiLetter(what[0]))
                    // Only a command known to maths is unescaped: \textbackslash section stays the text it was.
                    return new Token(Neutral.Contains(what) ? Kind.Neutral : MathOnly.Contains(what) || what is "text" or "mbox" ? Kind.Command : Kind.Stop,
                        esc.Length, @"\" + what + (NeedsSpace(s, i + esc.Length) ? " " : ""), what);
                // \textbackslash \{ is the brace delimiter \{; \textbackslash , a thin space.
                return what is "{" or "}" ? new Token(Kind.Symbol, esc.Length, @"\" + what, what)
                    : new Token(Kind.Neutral, esc.Length, @"\" + what + " ", what);
            }
            if (s.AsSpan(i).StartsWith(@"\textbackslash")) return new Token(Kind.Stop, @"\textbackslash".Length, "");
            foreach (var (written, maths, kind) in EscapedCharacters)
                if (s.AsSpan(i).StartsWith(written)) return new Token(kind, written.Length, maths);
            var name = BareName().Match(s, i);
            if (name.Success)
            {
                var n = name.Groups["name"].Value;
                if (TextStructure(n)) return new Token(Kind.Stop, name.Length, @"\" + n, n);
                return new Token(Neutral.Contains(n) ? Kind.Neutral : Kind.Command, name.Length,
                    @"\" + n + (NeedsSpace(s, i + name.Length) ? " " : ""), n);
            }
            // \, \; \! \: one-symbol commands; \\ a line break, which ends a run.
            if (i + 1 < s.Length && s[i + 1] is ',' or ';' or '!' or ':') return new Token(Kind.Neutral, 2, s.Substring(i, 2) + " ", s[(i + 1)..(i + 2)]);
            return new Token(Kind.Stop, i + 1 < s.Length ? 2 : 1, "");
        }
        if (c == '{') return new Token(Kind.Open, 1, "{");
        if (c == '}') return new Token(Kind.Close, 1, "}");
        if (c is '_' or '^') return new Token(Kind.Script, 1, c.ToString());
        if (c is ' ' or '\t' or '\r' or '\n')
        {
            // Spaces and one line break are one space; a blank line ends the run.
            var j = i;
            var lines = 0;
            while (j < s.Length && s[j] is ' ' or '\t' or '\r' or '\n') { if (s[j] == '\n') lines++; j++; }
            return new Token(Kind.Space, j - i, lines >= 2 ? "" : " ");
        }
        if (char.IsAsciiDigit(c) || c is '+' or '-' or '=' or '*' or '/' or '<' or '>' or '(' or ')' or '[' or ']' or '|' or ','
                or '.' or ';' or ':' or '\'' or '!' or '?')
            return new Token(Kind.Symbol, 1, c.ToString());
        if (char.IsAsciiLetter(c))
        {
            var j = i;
            while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
            return j - i == 1 ? new Token(Kind.Letter, 1, c.ToString()) : new Token(Kind.Word, j - i, s[i..j]);
        }
        return new Token(Kind.Stop, 1, "");
    }

    /// <summary>A command name written before a letter needs a space after it (\alpha x, not \alphax).</summary>
    private static bool NeedsSpace(string s, int after) => after < s.Length && char.IsAsciiLetter(s[after]);

    /// <summary>Characters Mathpix escapes in text, as maths.</summary>
    private static readonly (string Written, string Maths, Kind Kind)[] EscapedCharacters =
    [
        (@"\{", "{", Kind.Open), (@"\}", "}", Kind.Close), (@"\_", "_", Kind.Script),
        (@"\^{}", "^", Kind.Script), (@"\textasciicircum{}", "^", Kind.Script), (@"\textasciicircum ", "^", Kind.Script),
        (@"\textasciitilde{}", "~", Kind.Symbol), (@"\~{}", "~", Kind.Symbol), (@"\&", "&", Kind.Stop),
        (@"\textless{}", "<", Kind.Symbol), (@"\textgreater{}", ">", Kind.Symbol), (@"\textbar{}", "|", Kind.Symbol),
    ];

    /// <summary>\textbackslash (with {} or spaces after it), then a command name or one escaped character.</summary>
    [GeneratedRegex(@"\G\\textbackslash(?![A-Za-z])(?:\s*\{\s*\})?\s*(?:\\(?<what>[{}])|(?<what>[A-Za-z]+|[,;:!|]))")]
    private static partial Regex EscapedBackslashAt();

    [GeneratedRegex(@"\\textbackslash(?![A-Za-z])(?:\s*\{\s*\})?\s*(?:\\(?<what>[{}])|(?<what>[A-Za-z]+|[,;:!|]))")]
    private static partial Regex EscapedBackslash();

    [GeneratedRegex(@"\G\\(?<name>[A-Za-z]+)")]
    private static partial Regex BareName();

    [GeneratedRegex(@"\\(?:no)?limits(?![A-Za-z])")]
    private static partial Regex Limits();

    [GeneratedRegex(@"\\(?<name>[A-Za-z]+)\s*$")]
    private static partial Regex TrailingCommand();

    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)\\left(?![A-Za-z])")]
    private static partial Regex LeftCommand();

    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)\\right(?![A-Za-z])")]
    private static partial Regex RightCommand();

    [GeneratedRegex(@"\\begin\{(?<env>verbatim|lstlisting|minted|comment|Verbatim)\*?\}.*?\\end\{\k<env>\*?\}", RegexOptions.Singleline)]
    private static partial Regex Verbatim();

    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)%[^\n]*")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\\begin\{(?<env>equation|align|gather|multline|eqnarray|displaymath|math|flalign|alignat)(?<star>\*?)\}.*?\\end\{\k<env>\k<star>\}", RegexOptions.Singleline)]
    private static partial Regex MathEnvironment();
}
