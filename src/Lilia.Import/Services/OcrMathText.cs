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
        + "mathrel vec hat bar tilde dot ddot acute grave breve check mathring widehat widetilde overline overrightarrow "
        + "overleftarrow underbrace overbrace underset overset stackrel xrightarrow xleftarrow sqrt frac dfrac tfrac cfrac "
        + "binom dbinom tbinom pmod bmod "
        + "left right bigl bigr Bigl Bigr biggl biggr Biggl Biggr big Big bigg Bigg middle limits nolimits displaystyle "
        + "textstyle scriptstyle").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>Maths commands read back when they lost their backslash and still carry a script (iint_{D}).</summary>
    private static readonly HashSet<string> LostBackslash = new((
        "alpha beta gamma delta epsilon varepsilon zeta eta theta vartheta iota kappa lambda sigma varphi omega Gamma Delta "
        + "Theta Lambda Sigma Phi Psi Omega iint iiint oint sum prod coprod lim limsup liminf infty partial nabla sqrt frac "
        + "mathrm mathbf mathbb mathcal bigcup bigcap bigoplus bigotimes").Split(' '), StringComparer.Ordinal);

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
        body = Canonical(body, out wrapped);
        return source[..start] + body + source[end..];
    }

    /// <summary>
    /// A block's text (a paragraph, a heading, a list item, a caption, a cell) as LaTeX both TeX and Lilia's own text
    /// conversion read the same way (21 Nov 2026, the retest of notebooks A and C after API #678): maths balanced and mended,
    /// maths left in the text set as maths, formulas that touch joined (<c>$a$$b$</c> is <c>$ab$</c>: Lilia pairs
    /// <c>$$</c> first, TeX does not), a formula that starts with a number guarded (<c>$0, q$</c> read as the price "$0"),
    /// <c>\$</c> kept as a dollar. <see cref="ModeProblem"/> checks the result once rendered.
    /// </summary>
    public static string CanonicalText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOfAny(['$', '\\', '{', '}']) < 0) return text;
        // The LaTeX import keeps \$ as a bare $; Lilia then reads a $ before an amount as a price (CurrencyDollar). So does
        // this, unless the dollar opens a formula that closes in the paragraph and holds maths ($0, q\left(z\right)$): that
        // one stays a formula, written so Lilia can't read it as a price (Emit).
        text = Price().Replace(text, m => OpensFormula(text, m.Index) ? m.Value : @"\$");
        return Canonical(text, out _, blockText: true);
    }

    /// <summary>Whether the $ at <paramref name="at"/> has a closing $ before the paragraph ends, with maths between.</summary>
    private static bool OpensFormula(string text, int at)
    {
        for (var i = at + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '\n' && BlankLineAt(text, i)) return false;
            if (text[i] == '$')
            {
                var body = text[(at + 1)..i];
                return body.IndexOfAny(['\\', '^', '_', '{', '=']) >= 0;
            }
        }
        return false;
    }

    /// <summary>A dollar before an amount, as Lilia's CurrencyDollar reads it.</summary>
    [GeneratedRegex(@"(?<![\\$])\$(?=\d[\d,]*(?:\.\d+)?(?:\s+[A-Za-z]{2,}|\s*$|[,.;:!?)\]](?:\s|$)))")]
    private static partial Regex Price();

    private static string Canonical(string text, out int wrapped, bool blockText = false)
    {
        wrapped = 0;
        // Maths found in the text becomes formulas, which may touch others: read again until nothing changes.
        for (var pass = 0; pass < 3; pass++)
        {
            var segments = Segment(text, blockText);
            var count = 0;
            var sb = new StringBuilder(text.Length + 16);
            for (var s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                switch (seg.Kind)
                {
                    case SegmentKind.Text:
                        sb.Append(MathRuns(Unglued(seg.Body), ref count,
                            afterMath: s > 0 && segments[s - 1].IsMath, beforeMath: s + 1 < segments.Count && segments[s + 1].IsMath));
                        break;
                    case SegmentKind.Environment:
                        sb.Append(seg.Open).Append(MendMath(seg.Body, alignment: true)).Append(seg.Close);
                        break;
                    case SegmentKind.Kept:
                        sb.Append(seg.Body);
                        break;
                    default:
                        sb.Append(seg.Open).Append(MendMath(seg.Body, alignment: false)).Append(seg.Close);
                        break;
                }
            }
            var next = Emit(Segment(Join(sb.ToString()), blockText));
            wrapped += count;
            if (next == text) break;
            text = next;
        }
        return text;
    }

    /// <summary>
    /// Commands glued to what follows (<c>\qquadx</c>, and <c>\qquadé</c>: LuaTeX reads é as a letter of the name) set
    /// apart. In text and in formulas.
    /// </summary>
    private static string Unglued(string text)
    {
        if (text.IndexOf('\\') < 0) return text;
        text = GluedName().Replace(text, m => Unglue(m.Groups["name"].Value) is { } split ? split : m.Value);
        return GluedToLetter().Replace(text, "$1 ");
    }

    /// <summary>A command name followed by a letter outside ASCII.</summary>
    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)(\\[A-Za-z]+)(?=[^\x00-\x7F])")]
    private static partial Regex GluedToLetter();

    /// <summary>Where a run of maths touches other maths.</summary>
    private const char Junction = '';

    /// <summary>Two inline formulas a run made touch become one; next to a display, a space keeps them apart.</summary>
    private static string Join(string body)
    {
        if (body.IndexOf(Junction) < 0) return body;
        body = Regex.Replace(body, "(?<![$\\\\])\\$" + Junction + "\\$(?!\\$)", "");
        return body.Replace(Junction, ' ');
    }

    // ── 1. Segments, as TeX reads them ─────────────────────────────────────────────────────────────────────────────

    private enum SegmentKind { Text, Kept, Environment, Inline, Display }

    private sealed record Seg(SegmentKind Kind, string Open, string Body, string Close)
    {
        public bool IsMath => Kind is SegmentKind.Inline or SegmentKind.Display or SegmentKind.Environment;
    }

    /// <summary>Verbatim environments and comments: never touched.</summary>
    private static IEnumerable<(int Start, int End, bool Env)> Kept(string body, bool blockText) =>
        Verbatim().Matches(body).Select(m => (m.Index, m.Index + m.Length, false))
            // A block's text has no comments: its % is a percent sign (the LaTeX import unescapes \%).
            .Concat(blockText ? [] : Comment().Matches(body).Select(m => (m.Index, m.Index + m.Length, false)));

    /// <summary>
    /// The text cut as TeX reads it into text and maths, every formula closed where TeX would stop at the latest: before a
    /// blank line, before the <c>}</c> of the group it opened in, at the end. <c>\$</c> is a dollar, never a delimiter. A
    /// display closed by one <c>$</c> is closed; a closer with nothing open is dropped. Verbatim and comments are kept as
    /// they are; a maths environment is a formula of its own.
    /// </summary>
    private static List<Seg> Segment(string body, bool blockText = false)
    {
        var skip = Kept(body, blockText)
            .Concat(MathEnvironment().Matches(body).Select(m => (Start: m.Index, End: m.Index + m.Length, Env: true)))
            .OrderBy(r => r.Item1).ToList();
        var segments = new List<Seg>();
        var buf = new StringBuilder();
        string? open = null;          // "$", "$$", "\(", "\["
        var depth = 0;                // brace depth
        var openDepth = 0;            // the depth the maths opened at
        var k = 0;
        string Closer(string o) => o switch { "$" => "$", "$$" => "$$", @"\(" => @"\)", _ => @"\]" };
        void Flush()
        {
            if (open is null) { if (buf.Length > 0) segments.Add(new Seg(SegmentKind.Text, "", buf.ToString(), "")); }
            else segments.Add(new Seg(open is "$" or @"\(" ? SegmentKind.Inline : SegmentKind.Display, open, buf.ToString(), Closer(open)));
            buf.Clear();
        }
        // In a block's text, a $ never closed is a dollar: Lilia prints an unpaired $ as one.
        void Unclosed()
        {
            if (blockText && open is "$" or "$$")
            {
                var opener = open == "$" ? @"\$" : @"\$\$";
                var body = buf.ToString();
                buf.Clear();
                open = null;
                depth = openDepth;
                if (segments.Count > 0 && segments[^1].Kind == SegmentKind.Text)
                    segments[^1] = segments[^1] with { Body = segments[^1].Body + opener + body };
                else segments.Add(new Seg(SegmentKind.Text, "", opener + body, ""));
                return;
            }
            Close();
        }
        void Open(string o) { Flush(); open = o; openDepth = depth; }
        // A formula's own braces are balanced when it is mended: the text's depth is where the formula opened.
        void Close() { Flush(); open = null; depth = openDepth; }
        for (var i = 0; i < body.Length;)
        {
            while (k < skip.Count && skip[k].Item2 <= i) k++;
            if (k < skip.Count && skip[k].Item1 <= i)
            {
                if (open is not null) Close();     // a maths environment or a comment can't sit in open maths
                Flush();
                var (s, e, env) = skip[k];
                if (env)
                {
                    var m = MathEnvironment().Match(body, s);
                    var bodyGroup = m.Groups["body"];
                    segments.Add(new Seg(SegmentKind.Environment, body[s..bodyGroup.Index], bodyGroup.Value, body[(bodyGroup.Index + bodyGroup.Length)..e]));
                }
                else segments.Add(new Seg(SegmentKind.Kept, "", body[s..e], ""));
                i = e;
                continue;
            }
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                var n = body[i + 1];
                if (n is '(' or '[')
                {
                    if (open is null) Open(n == '(' ? @"\(" : @"\[");
                    // \( inside maths is nothing TeX can read: dropped.
                    i += 2;
                    continue;
                }
                if (n is ')' or ']')
                {
                    // The right closer, or the wrong one (the right one is written), close; with nothing open it is dropped.
                    if (open is not null) Close();
                    i += 2;
                    continue;
                }
                // An escaped character (\$ \{ \} \\ \%) is one token.
                buf.Append(c).Append(n);
                i += 2;
                continue;
            }
            if (c == '$')
            {
                var dbl = i + 1 < body.Length && body[i + 1] == '$';
                if (open is null) { Open(dbl ? "$$" : "$"); i += dbl ? 2 : 1; continue; }
                if (open == "$$") { Close(); i += dbl ? 2 : 1; continue; }   // a display ends with $$, even written $
                if (open == "$") { Close(); i += 1; continue; }              // a $ right after opens new maths: read on
                i += dbl ? 2 : 1;                                            // a $ inside \( or \[ is dropped
                continue;
            }
            if (c == '{') depth++;
            if (c == '}')
            {
                if (open is not null && depth <= openDepth) Close();
                depth--;
            }
            if (c == '\n' && open is not null && BlankLineAt(body, i)) Unclosed();
            buf.Append(c);
            i++;
        }
        if (open is not null) Unclosed();
        Flush();
        return segments;
    }

    /// <summary>
    /// The segments written back: touching inline formulas joined, an empty one dropped, a formula that starts with a
    /// number guarded with <c>{}</c> (Lilia reads <c>$0, q$</c> as a price), and a space between an inline formula and a
    /// display (<c>$a$$$b$$</c> pairs the wrong dollars). Each formula is written <c>$…$</c> or <c>$$…$$</c> as Lilia keeps
    /// them; <c>\[…\]</c> stays.
    /// </summary>
    private static string Emit(List<Seg> segments)
    {
        segments = BalanceTextBraces(segments);
        var merged = new List<Seg>();
        foreach (var seg in segments)
        {
            if (seg.Kind == SegmentKind.Inline && seg.Body.Trim().Length == 0) continue;
            if (seg.Kind == SegmentKind.Inline && merged.Count > 0 && merged[^1].Kind == SegmentKind.Inline)
            {
                merged[^1] = merged[^1] with { Body = merged[^1].Body + " " + seg.Body, Open = "$", Close = "$" };
                continue;
            }
            merged.Add(seg);
        }
        var sb = new StringBuilder();
        for (var s = 0; s < merged.Count; s++)
        {
            var seg = merged[s];
            if (s > 0 && seg.IsMath && merged[s - 1].IsMath) sb.Append(' ');
            switch (seg.Kind)
            {
                case SegmentKind.Text:
                case SegmentKind.Kept:
                    sb.Append(seg.Body);
                    break;
                case SegmentKind.Environment:
                    sb.Append(seg.Open).Append(seg.Body).Append(seg.Close);
                    break;
                case SegmentKind.Inline:
                    var inline = seg.Body;
                    if (inline.Length > 0 && char.IsAsciiDigit(inline.TrimStart()[0])) inline = "{}" + inline;
                    // A dollar right before it ("\$") would read as $$: a space between.
                    if (sb.Length > 0 && sb[^1] == '$') sb.Append(' ');
                    if (seg.Open == @"\(") sb.Append(@"\(").Append(inline).Append(@"\)");
                    else sb.Append('$').Append(inline).Append('$');
                    break;
                case SegmentKind.Display:
                    if (sb.Length > 0 && sb[^1] == '$') sb.Append(' ');
                    if (seg.Open == @"\[") sb.Append(@"\[").Append(seg.Body).Append(@"\]");
                    else sb.Append("$$").Append(seg.Body).Append("$$");
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The text's own braces balanced across its segments (a group may hold formulas: <c>\textbf{a $x$ b}</c>): a
    /// <c>}</c> that closes nothing, and a <c>{</c> never closed, are printed as braces (<c>\}</c>, <c>\{</c>).
    /// </summary>
    private static List<Seg> BalanceTextBraces(List<Seg> segments)
    {
        var opens = new Stack<(int Seg, int At)>();
        var escape = new HashSet<(int, int)>();
        for (var s = 0; s < segments.Count; s++)
        {
            if (segments[s].Kind != SegmentKind.Text) continue;
            var t = segments[s].Body;
            for (var i = 0; i < t.Length; i++)
            {
                if (t[i] == '\\') { i++; continue; }
                if (t[i] == '{') opens.Push((s, i));
                else if (t[i] == '}')
                {
                    if (opens.Count > 0) opens.Pop();
                    else escape.Add((s, i));
                }
            }
        }
        foreach (var o in opens) escape.Add(o);
        if (escape.Count == 0) return segments;
        var result = new List<Seg>(segments.Count);
        for (var s = 0; s < segments.Count; s++)
        {
            if (segments[s].Kind != SegmentKind.Text || !escape.Any(e => e.Item1 == s)) { result.Add(segments[s]); continue; }
            var t = segments[s].Body;
            var sb = new StringBuilder(t.Length + 4);
            for (var i = 0; i < t.Length; i++)
            {
                if (escape.Contains((s, i))) sb.Append('\\');
                sb.Append(t[i]);
            }
            result.Add(segments[s] with { Body = sb.ToString() });
        }
        return result;
    }

    /// <summary>The body with its delimiters balanced, nothing else changed (kept for the tests and callers of #678).</summary>
    internal static string BalanceDelimiters(string body)
    {
        var sb = new StringBuilder(body.Length + 8);
        foreach (var seg in Segment(body)) sb.Append(seg.Open).Append(seg.Body).Append(seg.Close);
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

    /// <summary>
    /// A formula's LaTeX made whole: escaped commands and characters unescaped, <c>\\iint</c> read as <c>\iint</c>,
    /// <c>\limits</c> after no operator dropped, an empty fraction part boxed, <c>%</c> and <c>#</c> (and <c>&amp;</c>
    /// outside an alignment) escaped, braces and <c>\left</c>/<c>\right</c> balanced.
    /// </summary>
    internal static string MendMath(string math, bool alignment = false)
    {
        if (string.IsNullOrEmpty(math)) return math;
        if (math.Contains(@"\text", StringComparison.Ordinal)) math = UnescapeInMath(math);
        // \\iint: a line break and the letters iint to TeX, OCR's \iint (notebook A, l.604, after API #678).
        math = BrokenCommandRaw().Replace(math, m => IsBrokenCommand(m) ? @"\" + m.Groups["name"].Value + m.Groups["next"].Value : m.Value);
        // \limitsx, \qquadx: a command glued to what follows it; \alpha\text{if} lost its \text{} to the LaTeX import's
        // text pass and became \alphaif. A space after each command keeps the next one's letters apart.
        math = Unglued(math);
        math = CommandBeforeCommand().Replace(math, "$1 ");
        math = DropStrayLimits(math);
        math = EmptyPart().Replace(math, m => m.Groups["head"].Value + @"{\square}");
        math = EmptyPart().Replace(math, m => m.Groups["head"].Value + @"{\square}");
        math = Unescaped().Replace(math, m => m.Value == "&" && (alignment || math.Contains(@"\begin{", StringComparison.Ordinal)) ? "&" : @"\" + m.Value);
        // A line break has no place in an inline formula (one in an array or a matrix stays).
        if (!alignment && !math.Contains(@"\begin{", StringComparison.Ordinal)) math = math.Replace(@"\\", " ");
        return FixScripts(Balance(math));
    }

    /// <summary>
    /// <c>\name</c> TeX doesn't know, made of a maths command and one letter or another command glued to it
    /// (<c>\limitsx</c>, <c>\qquadiint</c>): the two apart, or null.
    /// </summary>
    private static string? Unglue(string name)
    {
        if (MathOnly.Contains(name) || Neutral.Contains(name) || KnownElse.Contains(name)) return null;
        for (var cut = name.Length - 1; cut >= 2; cut--)
        {
            var head = name[..cut];
            var rest = name[cut..];
            // One letter or a short word glued on (\qquadx, \alphaif: \text{if} lost its braces), or another command.
            if ((MathOnly.Contains(head) || Neutral.Contains(head)) && head.Length >= 3 && (rest.Length <= 3 || MathOnly.Contains(rest)))
                return @"\" + head + " " + (MathOnly.Contains(rest) && rest.Length > 3 ? @"\" + rest : rest);
        }
        return null;
    }

    /// <summary>Commands that start like a maths command and are not two glued (amssymb's \leqq, \triangleq …).</summary>
    private static readonly HashSet<string> KnownElse = new((
        "leqq geqq nleq ngeq nleqq ngeqq subseteqq supseteqq subsetneqq supsetneqq triangleq trianglelefteq trianglerighteq "
        + "triangleleft triangleright bigtriangleup bigtriangledown blacktriangle sqsubseteq sqsupseteq ggg lll circeq "
        + "leftrightarrows leftleftarrows rightrightarrows rightarrowtail leftarrowtail leftharpoonup rightharpoonup "
        + "leftrightharpoons longleftarrow intercal iiiint idotsint operatornamewithlimits textbackslash textbf textit textrm "
        + "textsf texttt textnormal textup textsc infer cdotp ldotp vdash dashv inftyx").Split(' '), StringComparer.Ordinal);

    /// <summary>
    /// Every sub- and superscript given something to sit on: one before nothing (<c>x^</c>, <c>\left[^ \right]</c>), before
    /// <c>\left</c> or a command that takes arguments (<c>^\vec{v}</c>) gets braces, and a second one of the same kind on
    /// one nucleus (<c>\oint_{}_</c>, "Double subscript") a <c>{}</c> before it.
    /// </summary>
    internal static string FixScripts(string math) => FixScripts(math, out _);

    /// <summary>Commands that can't be a script's argument unbraced: they expand to several tokens (\int is \DOTSI\intop\ilimits@).</summary>
    private static bool NeedsBraces(string name) =>
        Arity(name) > 0 || Operators.Contains(name) || name is "cdots" or "ldots" or "dots" or "vdots" or "ddots" or "not" or "log"
            or "ln" or "exp" or "sin" or "cos" or "tan" or "cot" or "sec" or "csc" or "arcsin" or "arccos" or "arctan" or "sinh"
            or "cosh" or "tanh" or "arg" or "deg" or "dim" or "hom" or "ker" or "lg" or "coth" or "displaystyle" or "textstyle"
            or "scriptstyle" or "quad" or "qquad" or "iff" or "implies" or "text" or "mbox";

    /// <param name="issue">Whether TeX would have stopped: a script with nothing to sit on, a second one on one nucleus, or
    /// one whose argument must be braced. Bracing a plain command (x_\alpha) is no issue.</param>
    internal static string FixScripts(string math, out bool issue)
    {
        issue = false;
        if (math.IndexOfAny(['_', '^']) < 0) return math;
        var sb = new StringBuilder(math.Length + 8);
        bool sub = false, sup = false;
        for (var i = 0; i < math.Length; i++)
        {
            var c = math[i];
            if (c == '\\' && i + 1 < math.Length)
            {
                var j = i + 1;
                if (char.IsAsciiLetter(math[j])) while (j < math.Length && char.IsAsciiLetter(math[j])) j++;
                else j++;
                sb.Append(math, i, j - i);
                // \limits and \nolimits belong to the nucleus; anything else starts a new one.
                var nm = math[(i + 1)..j];
                if (nm is not ("limits" or "nolimits")) { sub = false; sup = false; }
                i = j - 1;
                continue;
            }
            if (c is '_' or '^')
            {
                if (c == '_' ? sub : sup) { sb.Append("{}"); sub = false; sup = false; issue = true; }
                if (c == '_') sub = true; else sup = true;
                sb.Append(c);
                var k = i + 1;
                while (k < math.Length && math[k] == ' ') k++;
                var end = ScriptArgumentEnd(math, k);
                if (end < 0) { sb.Append("{}"); issue = true; continue; }
                var arg = math[k..end];
                // A command as the argument is braced: ^\vec{v} and _\int stop TeX unbraced; x_\alpha does not, braced all the same.
                if (arg.Length > 1 && arg[0] == '\\' && char.IsAsciiLetter(arg[1]))
                {
                    if (NeedsBraces(TrailingName(arg)!)) issue = true;
                    sb.Append('{').Append(arg).Append('}');
                }
                else sb.Append(math, i + 1, end - i - 1);
                i = end - 1;
                continue;
            }
            if (c is not (' ' or '\'')) { sub = false; sup = false; }
            if (c == '{')
            {
                // A whole group is one nucleus: copied as it is, its own scripts mended.
                var close = Closing(math, i);
                if (close > 0)
                {
                    sb.Append('{').Append(FixScripts(math[(i + 1)..close], out var inner)).Append('}');
                    issue |= inner;
                    i = close;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? TrailingName(string arg)
    {
        var j = 1;
        while (j < arg.Length && char.IsAsciiLetter(arg[j])) j++;
        return arg[1..j];
    }

    /// <summary>The end of a script's argument at <paramref name="k"/> (a group, a command with its arguments, one character), or -1 when there is none.</summary>
    private static int ScriptArgumentEnd(string math, int k)
    {
        if (k >= math.Length) return -1;
        var c = math[k];
        if (c is '}' or '&' or '_' or '^' or '$') return -1;
        if (c == '{') { var close = Closing(math, k); return close < 0 ? -1 : close + 1; }
        if (c != '\\') return k + 1;
        if (k + 1 >= math.Length) return -1;
        // \{ \} \| \# \% \& \$ are symbols; \, \; \: \! and \\ give nothing to sit on.
        if (!char.IsAsciiLetter(math[k + 1])) return math[k + 1] is '{' or '}' or '|' or '#' or '%' or '&' or '$' ? k + 2 : -1;
        var j = k + 1;
        while (j < math.Length && char.IsAsciiLetter(math[j])) j++;
        var name = math[(k + 1)..j];
        if (name is "left" or "right" or "middle" or "big" or "Big" or "bigg" or "Bigg" or "limits" or "nolimits") return -1;
        var arity = Arity(name);
        if (name == "sqrt" && j < math.Length && math[j] == '[') { var sq = math.IndexOf(']', j); if (sq > 0) j = sq + 1; }
        for (var a = 0; a < arity; a++)
        {
            var t = j;
            while (t < math.Length && math[t] == ' ') t++;
            if (t < math.Length && math[t] == '{') { var close = Closing(math, t); if (close < 0) break; j = close + 1; }
            else if (t < math.Length && !char.IsWhiteSpace(math[t]) && math[t] is not ('}' or '$')) j = t + 1;
            else break;
        }
        return j;
    }

    /// <summary>What would stop TeX inside a formula's body that the dollars can't show, or null.</summary>
    private static string? FormulaProblem(string body)
    {
        FixScripts(body, out var issue);
        if (issue) return "a script with nothing to sit on, a second one on one nucleus, or one that must be braced";
        foreach (Match m in GluedName().Matches(body))
            if (Unglue(m.Groups["name"].Value) is not null) return $"\\{m.Groups["name"].Value} is two commands glued";
        return null;
    }

    /// <summary>\\name is OCR's \name when the name is a maths command of three letters or more, or has a script or group after it.</summary>
    private static bool IsBrokenCommand(Match m) =>
        MathOnly.Contains(m.Groups["name"].Value) && (m.Groups["name"].Value.Length >= 3 || m.Groups["next"].Success);

    /// <summary>An equation block's LaTeX mended as a formula that may align (its &amp; and \\ stay).</summary>
    public static string MendFormula(string latex) => string.IsNullOrEmpty(latex) ? latex : MendMath(latex, alignment: true);

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

    /// <summary>
    /// Unmatched <c>}</c> dropped, missing ones added; a <c>\right</c> with no <c>\left</c> before it gets <c>\left.</c>,
    /// and a <c>\left</c> left open gets <c>\right.</c> at the end.
    /// </summary>
    internal static string Balance(string math)
    {
        var sb = new StringBuilder(math.Length + 8);
        var depth = 0;
        var pairs = 0;
        for (var i = 0; i < math.Length; i++)
        {
            var c = math[i];
            if (c == '\\' && i + 1 < math.Length)
            {
                var isLeft = At(math, i, @"\left") && !NextIsLetter(math, i + 5);
                var isRight = !isLeft && At(math, i, @"\right") && !NextIsLetter(math, i + 6);
                if (isLeft || isRight)
                {
                    if (isLeft) pairs++;
                    else if (pairs == 0) sb.Append(@"\left. ");
                    else pairs--;
                    var after = i + (isLeft ? 5 : 6);
                    sb.Append(isLeft ? @"\left" : @"\right");
                    // Its delimiter, or "." when what follows is none ("Missing delimiter (. inserted)").
                    if (DelimiterLength(math, after) == 0) sb.Append('.');
                    i = after - 1;
                    continue;
                }
                sb.Append(c).Append(math[i + 1]);
                i++;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                if (depth == 0) continue;
                depth--;
            }
            sb.Append(c);
        }
        if (depth > 0) sb.Append('}', depth);
        for (var p = 0; p < pairs; p++) sb.Append(@" \right.");
        return sb.ToString();
    }

    private static readonly string[] DelimiterCommands =
    [
        @"\{", @"\}", @"\|", @"\langle", @"\rangle", @"\lfloor", @"\rfloor", @"\lceil", @"\rceil", @"\vert", @"\Vert", @"\lvert",
        @"\rvert", @"\lVert", @"\rVert", @"\uparrow", @"\downarrow", @"\updownarrow", @"\Uparrow", @"\Downarrow", @"\backslash",
    ];

    /// <summary>How long the delimiter at <paramref name="at"/> is (spaces before it included), or 0 when there is none.</summary>
    private static int DelimiterLength(string s, int at)
    {
        var i = at;
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length) return 0;
        if (s[i] is '(' or ')' or '[' or ']' or '|' or '/' or '.' or '<' or '>') return i + 1 - at;
        foreach (var d in DelimiterCommands)
            if (At(s, i, d) && !(char.IsAsciiLetter(d[^1]) && NextIsLetter(s, i + d.Length))) return i + d.Length - at;
        return 0;
    }

    private static bool At(string s, int i, string what) => string.CompareOrdinal(s, i, what, 0, what.Length) == 0;

    private static bool NextIsLetter(string s, int i) => i < s.Length && char.IsAsciiLetter(s[i]);

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

    private static int Closing(string s, int open)
    {
        var depth = 0;
        for (var k = open; k < s.Length; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '{') depth++;
            else if (s[k] == '}' && --depth == 0) return k;
        }
        return -1;
    }

    // ── The check: LaTeX as TeX reads it ───────────────────────────────────────────────────────────────────────────

    /// <summary>Commands text has too, though they are maths: never reported in text.</summary>
    private static readonly HashSet<string> TextToo = new("underline dots ldots quad qquad".Split(' '), StringComparer.Ordinal);

    private static readonly HashSet<string> AlignmentEnvironments = new(
        ("equation equation* align align* gather gather* multline multline* eqnarray eqnarray* displaymath math flalign flalign* "
         + "alignat alignat* aligned gathered split array matrix pmatrix bmatrix Bmatrix vmatrix Vmatrix smallmatrix cases "
         + "tabular tabular* tabularx longtable tblr").Split(' '), StringComparer.Ordinal);

    private static readonly HashSet<string> MathEnvironments = new(
        "equation equation* align align* gather gather* multline multline* eqnarray eqnarray* displaymath math flalign flalign* alignat alignat*"
            .Split(' '), StringComparer.Ordinal);

    /// <summary>
    /// What would stop TeX in a block's rendered LaTeX, read in TeX's text and maths modes, or null: a dollar left
    /// unpaired, <c>$$</c> where no display is meant (two formulas touching), a display closed by one <c>$</c>, a blank
    /// line in maths, a brace closing what a formula did not open, braces unbalanced, a maths-only command, <c>_</c> or
    /// <c>^</c> in text, <c>\textbackslash</c> in maths, <c>\left</c> and <c>\right</c> unpaired within a formula, an
    /// unescaped <c>%</c> or <c>#</c>, <c>&amp;</c> outside an alignment.
    /// </summary>
    public static string? ModeProblem(string latex)
    {
        if (string.IsNullOrEmpty(latex)) return null;
        string? math = null;            // "$", "$$", "\(", "\[", or an environment's name
        var mathDepth = 0;
        var mathStart = 0;              // where the formula's body starts
        var depth = 0;
        var pairs = 0;
        var envs = new Stack<string>();
        for (var i = 0; i < latex.Length; i++)
        {
            var c = latex[i];
            if (c == '\\')
            {
                if (i + 1 >= latex.Length) return "a \\ at the end";
                var n = latex[i + 1];
                if (!char.IsAsciiLetter(n))
                {
                    if (n is '(' or '[')
                    {
                        if (math is not null) return $"\\{n} inside maths";
                        math = n == '(' ? @"\(" : @"\["; mathDepth = depth; pairs = 0; mathStart = i + 2;
                    }
                    else if (n is ')' or ']')
                    {
                        if (math != (n == ')' ? @"\(" : @"\[")) return $"\\{n} closes nothing";
                        if (pairs != 0) return "\\left and \\right are not paired in a formula";
                        if (FormulaProblem(latex[mathStart..i]) is { } fp) return fp;
                        math = null;
                    }
                    i++;
                    continue;
                }
                var j = i + 1;
                while (j < latex.Length && char.IsAsciiLetter(latex[j])) j++;
                var name = latex[(i + 1)..j];
                if (name is "begin" or "end" && j < latex.Length && latex[j] == '{')
                {
                    var close = latex.IndexOf('}', j);
                    if (close < 0) return "\\begin without its name";
                    var env = latex[(j + 1)..close];
                    if (name == "begin" && env is "lstlisting" or "verbatim" or "Verbatim" or "minted" or "comment")
                    {
                        // Verbatim: read as written, to its end.
                        var endAt = latex.IndexOf(@"\end{" + env + "}", close, StringComparison.Ordinal);
                        if (endAt < 0) return $"\\begin{{{env}}} is never ended";
                        i = endAt + env.Length + 5;
                        continue;
                    }
                    if (name == "begin")
                    {
                        envs.Push(env);
                        if (math is null && MathEnvironments.Contains(env)) { math = env; mathDepth = depth; pairs = 0; mathStart = close + 1; }
                    }
                    else
                    {
                        if (envs.Count == 0 || envs.Peek() != env) return $"\\end{{{env}}} closes nothing";
                        envs.Pop();
                        if (math == env)
                        {
                            if (pairs != 0) return "\\left and \\right are not paired in a formula";
                            if (FormulaProblem(latex[mathStart..i]) is { } fp) return fp;
                            math = null;
                        }
                    }
                    i = close;
                    continue;
                }
                // A reference, a link or a file name is read as written: its argument is no text.
                if (name is "url" or "href" or "label" or "ref" or "eqref" or "cref" or "Cref" or "autoref" or "pageref" or "nameref"
                        or "cite" or "citep" or "citet" or "citealp" or "nocite" or "parencite" or "textcite" or "includegraphics"
                        or "hypertarget" or "hyperref" or "input" or "include" or "bibliography" or "bibliographystyle")
                {
                    var k = j;
                    while (k < latex.Length && latex[k] is '*' or ' ') k++;
                    if (k < latex.Length && latex[k] == '[') { var sq = latex.IndexOf(']', k); if (sq > 0) k = sq + 1; }
                    if (k < latex.Length && latex[k] == '{')
                    {
                        var close = Closing(latex, k);
                        if (close < 0) return "a { is never closed";
                        i = close;
                        continue;
                    }
                }
                // \ensuremath{…} is maths wherever it stands (OcrMathGuard's box).
                if (name == "ensuremath" && j < latex.Length && latex[j] == '{' && math is null)
                {
                    var close = Closing(latex, j);
                    if (close < 0) return "a { is never closed";
                    if (ModeProblem("$" + latex[(j + 1)..close] + "$") is { } inner) return inner;
                    i = close;
                    continue;
                }
                if (math is null && MathOnly.Contains(name) && !TextToo.Contains(name)) return $"\\{name} in text";
                if (Unglue(name) is not null) return $"\\{name} is two things glued";
                if (j < latex.Length && char.IsLetter(latex[j])) return $"\\{name} is glued to a letter TeX reads as part of its name";
                if (math is not null && name == "textbackslash") return "\\textbackslash in maths";
                if (math is not null && name is "left" or "right" && DelimiterLength(latex, j) == 0) return $"\\{name} without a delimiter";
                if (math is not null && name == "left") pairs++;
                if (math is not null && name == "right" && --pairs < 0) return "\\right before its \\left";
                i = j - 1;
                continue;
            }
            switch (c)
            {
                case '%':
                    // A comment: in maths it hides the rest of the formula's line.
                    if (math is not null) return "an unescaped % in maths (the rest of the line is a comment)";
                    var eol = latex.IndexOf('\n', i);
                    i = eol < 0 ? latex.Length : eol - 1;
                    break;
                case '#' when math is not null:
                    return "an unescaped # in maths";
                case '&' when math is not null && !envs.Any(AlignmentEnvironments.Contains):
                    return "& in a formula outside an alignment";
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth < 0) return "a } closes nothing";
                    if (math is not null && depth < mathDepth) return "a } closes a group the formula did not open";
                    break;
                case '_' or '^' when math is null && !envs.Any(AlignmentEnvironments.Contains):
                    return $"{c} in text";
                case '\n' when math is not null && BlankLineAt(latex, i) && !MathEnvironments.Contains(math):
                    return "a blank line in maths";
                case '$':
                    var dbl = i + 1 < latex.Length && latex[i + 1] == '$';
                    if (math is null)
                    {
                        math = dbl ? "$$" : "$"; mathDepth = depth; pairs = 0;
                        if (dbl) i++;
                        mathStart = i + 1;
                    }
                    else if (math == "$")
                    {
                        if (dbl) return "two formulas touch: $$ where no display is meant";
                        if (pairs != 0) return "\\left and \\right are not paired in a formula";
                        if (FormulaProblem(latex[mathStart..i]) is { } fp) return fp;
                        math = null;
                    }
                    else if (math == "$$")
                    {
                        if (!dbl) return "a display closed by one $";
                        if (pairs != 0) return "\\left and \\right are not paired in a formula";
                        if (FormulaProblem(latex[mathStart..i]) is { } fp) return fp;
                        math = null; i++;
                    }
                    else return "a $ inside maths";
                    break;
            }
        }
        if (math is not null) return "a formula is never closed";
        if (depth != 0) return "a { is never closed";
        return null;
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
            or "pmod" or "text" or "textrm" or "textit" or "textbf" or "textsf" or "texttt" or "textnormal" or "mbox" or "hbox" => 1,
        _ => 0,
    };

    private static readonly HashSet<string> Sized = new(
        "left right middle bigl bigr Bigl Bigr biggl biggr Biggl Biggr big Big bigg Bigg".Split(' '), StringComparer.Ordinal);

    /// <summary>The text with every run of maths in it set as inline maths.</summary>
    private static string MathRuns(string text, ref int count, bool afterMath = false, bool beforeMath = false)
    {
        if (text.IndexOf('\\') < 0 && text.IndexOfAny(['_', '^']) < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        var i = 0;
        while (i < text.Length)
        {
            var t = Read(text, i);
            if (t.Kind == Kind.Command && MathOnly.Contains(t.Name!) && Run(text, i) is { } run)
            {
                // Next to other maths ($x$\alpha): a mark, where the two become one formula (Join). $x$$\alpha$ would pair
                // the wrong dollars in Lilia's text, and the LaTeX import drops a {} between them.
                // A one-letter function before a delimiter is the formula's: q\left(z\right) is $q\left(z\right)$ (notebook C).
                var maths = run.Maths;
                if (t.Name is { } nm && Sized.Contains(nm) && LoneLetterAtEnd(sb) is { } letter)
                {
                    sb.Length -= 1;
                    maths = letter + maths;
                }
                if (sb.Length > 0 ? sb[^1] == '$' : afterMath) sb.Append(Junction);
                sb.Append('$').Append(maths).Append('$');
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

    /// <summary>The single letter the text so far ends with, standing alone (after a space, a bracket or the start), or null.</summary>
    private static char? LoneLetterAtEnd(StringBuilder sb)
    {
        if (sb.Length == 0 || !char.IsAsciiLetter(sb[^1])) return null;
        if (sb.Length >= 2 && (char.IsLetterOrDigit(sb[^2]) || sb[^2] == '\\')) return null;
        return sb[^1];
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
            // \\iint: a line break glued to a maths command, OCR's \iint (notebook A).
            var broken = BrokenCommandRaw().Match(s, i);
            if (broken.Success && broken.Index == i && IsBrokenCommand(broken))
            {
                var bn = broken.Groups["name"].Value;
                var len = 2 + bn.Length;
                return new Token(Kind.Command, len, @"\" + bn + (NeedsSpace(s, i + len) ? " " : ""), bn);
            }
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
            // An address (https://cdn.mathpix.com/…_top_left_y=…) is never maths: read whole, to its end.
            if (s.AsSpan(j).StartsWith("://"))
            {
                var e = j;
                while (e < s.Length && !char.IsWhiteSpace(s[e]) && s[e] is not (')' or ']' or '}')) e++;
                return new Token(Kind.Stop, e - i, "");
            }
            if (j - i == 1) return new Token(Kind.Letter, 1, c.ToString());
            // iint_{D}: a maths command that lost its backslash, known by its script (notebook A, l.604). Only a name no
            // word or identifier shares (not top_left, max_size), standing on its own.
            var word = s[i..j];
            if (LostBackslash.Contains(word) && (i == 0 || s[i - 1] is ' ' or '\t' or '\n' or '(' or '[' or '=' or ',')
                    && j < s.Length && (s[j] is '_' or '^' || s.AsSpan(j).StartsWith(@"\_")))
                return new Token(Kind.Command, j - i, @"\" + word, word);
            return new Token(Kind.Word, j - i, word);
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

    [GeneratedRegex(@"\\begin\{(?<env>equation|align|gather|multline|eqnarray|displaymath|math|flalign|alignat)(?<star>\*?)\}(?<body>.*?)\\end\{\k<env>\k<star>\}", RegexOptions.Singleline)]
    private static partial Regex MathEnvironment();

    /// <summary>\\ glued to a command name: \\iint, \\alpha_1.</summary>
    [GeneratedRegex(@"(?<!\\)\\\\(?<name>[A-Za-z]+)(?<next>[_^{])?")]
    private static partial Regex BrokenCommandRaw();

    /// <summary>A command name (its backslash not itself escaped).</summary>
    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)\\(?<name>[A-Za-z]{3,})")]
    private static partial Regex GluedName();

    /// <summary>A command name right before another command.</summary>
    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)(\\[A-Za-z]+)(?=\\[A-Za-z])")]
    private static partial Regex CommandBeforeCommand();

    /// <summary>An empty part of a fraction, a binomial or a root.</summary>
    [GeneratedRegex(@"(?<head>\\(?:[dtc]?frac|[dt]?binom)\s*(?:\{(?:[^{}]|\{[^{}]*\})*\}\s*)?|\\sqrt\s*(?:\[[^\]]*\])?\s*)\{\s*\}")]
    private static partial Regex EmptyPart();

    /// <summary>%, # or &amp; whose backslash is not there.</summary>
    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)[%#&]")]
    private static partial Regex Unescaped();
}
