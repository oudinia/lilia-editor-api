using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// The definition statements of a LaTeX source that belong in the preamble and are never text: xparse commands and
/// environments, <c>\ExplSyntaxOn … \ExplSyntaxOff</c> and <c>\makeatletter … \makeatother</c> blocks, font and
/// symbol declarations, colours, lengths and counters, and <c>\let</c>.
///
/// <para>The launch bot's TeX Stack Exchange run (14 Nov 2026, #99 and #102): an import kept only <c>\newcommand</c>,
/// <c>\newenvironment</c>, <c>\def</c> and <c>\DeclareMathOperator</c> (LatexPreambleExtractor), so an answer's
/// <c>\NewDocumentCommand</c>, expl3 code or <c>\DeclareSymbolFont</c> was lost and the body didn't compile; and in
/// the body, the flattener ate an unknown command and kept its arguments: <c>\colorlet{quack}{black}</c> printed
/// "quack{black}".</para>
///
/// <para>Arguments are read with balanced braces at any depth (the older patterns went one level deep, which an
/// xparse environment's body passes at once), and each command reads only the arguments it takes, so a statement
/// never swallows the text after it.</para>
/// </summary>
public static class LatexDefinitions
{
    // The argument shape of each command: 'c' a control sequence (\name, or {\name}); 'm' a braced group (or one
    // token); 'o' an optional [..] group, absent or present; 's' an optional star; 'p' \\def's parameter text.
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        // xparse (in the LaTeX kernel since 2020)
        ["NewDocumentCommand"] = "cmm", ["RenewDocumentCommand"] = "cmm", ["ProvideDocumentCommand"] = "cmm",
        ["DeclareDocumentCommand"] = "cmm", ["NewExpandableDocumentCommand"] = "cmm",
        ["NewDocumentEnvironment"] = "mmmm", ["RenewDocumentEnvironment"] = "mmmm",
        ["ProvideDocumentEnvironment"] = "mmmm", ["DeclareDocumentEnvironment"] = "mmmm",
        // Fonts and maths symbols
        ["DeclareSymbolFont"] = "mmmmm", ["SetSymbolFont"] = "mmmmmm", ["DeclareSymbolFontAlphabet"] = "cm",
        ["DeclareMathAlphabet"] = "cmmmm", ["SetMathAlphabet"] = "cmmmmm", ["DeclareMathSymbol"] = "cmmm",
        ["DeclareMathDelimiter"] = "cmmmmm", ["DeclareMathAccent"] = "cmmm", ["DeclareMathRadical"] = "cmmmm",
        ["DeclareFontFamily"] = "mmm", ["DeclareFontShape"] = "mmmmmm", ["DeclareRobustCommand"] = "socoom",
        // Colours (xcolor)
        ["definecolor"] = "ommm", ["colorlet"] = "omom", ["providecolor"] = "ommm",
        // Lengths, counters
        ["newlength"] = "c", ["newcounter"] = "mo", ["newif"] = "c", ["setlength"] = "cm", ["addtolength"] = "cm",
        // The LaTeX macros themselves, read here at any depth: the older patterns went one brace level deep, so a body
        // like \rot{\parbox[c]{..}{..}} leaked as text (launch bot #105, 14 Nov 2026).
        ["newcommand"] = "scoom", ["renewcommand"] = "scoom", ["providecommand"] = "scoom",
        ["newenvironment"] = "smoomm", ["renewenvironment"] = "smoomm",
        ["def"] = "cpm", ["gdef"] = "cpm", ["edef"] = "cpm", ["xdef"] = "cpm",
        // array's column types
        ["newcolumntype"] = "mom",
    };

    private static readonly (string Open, string Close)[] Blocks =
    [
        (@"\makeatletter", @"\makeatother"),
        (@"\ExplSyntaxOn", @"\ExplSyntaxOff"),
    ];

    private static readonly Regex CommandName = new(@"\\([A-Za-z@]+)", RegexOptions.Compiled);

    /// <summary>Every definition statement, in source order, outside comments: (start, length).</summary>
    public static List<(int Start, int Length)> Find(string source)
    {
        var found = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(source)) return found;

        // What is shown or drawn as written is never a definition of the document: code listings and verbatim text,
        // and TikZ pictures, where \def\r{2} is local to the picture.
        var verbatim = Regex.Matches(source, @"\\begin\{(verbatim\*?|Verbatim|lstlisting|minted|comment)\}[\s\S]*?\\end\{\1\}")
            .Select(m => (Start: m.Index, Length: m.Length))
            .Concat(Lilia.Core.Blocks.TikzFigure.FindEnvironments(source).Select(p => (p.Start, p.Length)))
            .ToList();
        bool Kept(int pos) => verbatim.Any(v => pos >= v.Start && pos < v.Start + v.Length);

        // Blocks first: what they hold is theirs (a \newcommand inside \makeatletter is the block's).
        foreach (var (open, close) in Blocks)
        {
            var at = 0;
            while ((at = source.IndexOf(open, at, StringComparison.Ordinal)) >= 0)
            {
                if (InComment(source, at) || Kept(at)) { at += open.Length; continue; }
                var end = source.IndexOf(close, at + open.Length, StringComparison.Ordinal);
                if (end < 0) break;
                found.Add((at, end + close.Length - at));
                at = end + close.Length;
            }
        }
        bool Inside(int pos) => found.Any(f => pos >= f.Start && pos < f.Start + f.Length);

        foreach (Match m in CommandName.Matches(source))
        {
            if (!Shapes.TryGetValue(m.Groups[1].Value, out var shape)) continue;
            // \foo inside \foobar is not \foo (the regex already takes the whole name); skip nested and commented.
            if (Inside(m.Index) || InComment(source, m.Index) || Kept(m.Index)) continue;
            var end = ReadArguments(source, m.Index + m.Length, shape);
            if (end is int e) found.Add((m.Index, e - m.Index));
        }

        // \let\a\b and \let\a=\b
        foreach (Match m in Regex.Matches(source, @"\\let\s*\\[A-Za-z@]+\s*=?\s*\\[A-Za-z@]+"))
            if (!Inside(m.Index) && !InComment(source, m.Index) && !Kept(m.Index)) found.Add((m.Index, m.Length));

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    /// <summary>The statements' text, in order.</summary>
    public static IEnumerable<(int Start, string Text)> Statements(string source) =>
        Find(source).Select(f => (f.Start, source.Substring(f.Start, f.Length).Trim()));

    /// <summary>The source without its definition statements.</summary>
    public static string Strip(string source)
    {
        var found = Find(source);
        if (found.Count == 0) return source;
        var sb = new System.Text.StringBuilder(source.Length);
        var at = 0;
        foreach (var (start, length) in found)
        {
            if (start < at) continue;
            sb.Append(source, at, start - at);
            at = start + length;
        }
        sb.Append(source, at, source.Length - at);
        return sb.ToString();
    }

    /// <summary>Where the arguments end, or null when the statement isn't complete.</summary>
    private static int? ReadArguments(string s, int i, string shape)
    {
        foreach (var slot in shape)
        {
            var j = SkipSpace(s, i);
            switch (slot)
            {
                case 'p':
                    // \def's parameter text (#1#2, or delimiters) up to its body; never across a line.
                    var brace = s.IndexOf('{', i);
                    if (brace < 0 || brace - i > 40 || s.AsSpan(i, brace - i).IndexOfAny('\n', '}') >= 0) return null;
                    i = brace;
                    break;
                case 's':
                    if (j < s.Length && s[j] == '*') i = j + 1;
                    break;
                case 'o':
                    if (j < s.Length && s[j] == '[')
                    {
                        var close = Balanced(s, j, '[', ']');
                        if (close is null) return null;
                        i = close.Value + 1;
                    }
                    break;
                case 'c':
                    if (j < s.Length && s[j] == '\\')
                    {
                        var name = CommandName.Match(s, j);
                        if (!name.Success || name.Index != j) return null;
                        i = j + name.Length;
                    }
                    else if (j < s.Length && s[j] == '{')
                    {
                        var close = Balanced(s, j, '{', '}');
                        if (close is null) return null;
                        i = close.Value + 1;
                    }
                    else return null;
                    break;
                case 'm':
                    if (j >= s.Length) return null;
                    if (s[j] == '{')
                    {
                        var close = Balanced(s, j, '{', '}');
                        if (close is null) return null;
                        i = close.Value + 1;
                    }
                    else if (s[j] == '\\')
                    {
                        var name = CommandName.Match(s, j);
                        i = name.Success && name.Index == j ? j + name.Length : j + 2;
                    }
                    else i = j + 1;
                    break;
            }
        }
        return i;
    }

    private static int SkipSpace(string s, int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
        {
            // A blank line ends a statement's arguments.
            if (s[i] == '\n' && s.IndexOf('\n', i + 1) is var next && next > i && string.IsNullOrWhiteSpace(s[(i + 1)..next])) return i;
            i++;
        }
        return i;
    }

    /// <summary>The index of the bracket closing the one at <paramref name="open"/>, skipping escaped ones and comments.</summary>
    private static int? Balanced(string s, int open, char o, char c)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '\\') { i++; continue; }
            if (ch == '%') { var nl = s.IndexOf('\n', i); if (nl < 0) return null; i = nl; continue; }
            if (ch == o) depth++;
            else if (ch == c && --depth == 0) return i;
        }
        return null;
    }

    private static bool InComment(string s, int pos)
    {
        var lineStart = s.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1;
        for (var i = lineStart; i < pos; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '%') return true;
        }
        return false;
    }
}
