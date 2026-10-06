using System.Text;

namespace Lilia.Engines;

/// <summary>
/// Turning user-typed text into LaTeX source.
///
/// <para>This existed as seven separate copies — in RenderService (three times),
/// LaTeXExportService, LmlConversionService, ConvertController and the tools
/// runner — and every one carried the same defect. They escaped by chained
/// <c>Replace</c>: <c>\</c> became <c>\textbackslash{}</c> first, and the
/// <c>{</c> / <c>}</c> replacements that ran afterwards then escaped the braces
/// that step had just inserted. A single backslash in user text came out as
/// <c>\textbackslash\{\}</c>, which renders as <c>\{}</c>.</para>
///
/// <para>A single pass over the characters makes that class of bug impossible:
/// output is never re-examined, so nothing an escape emits can be escaped again.</para>
/// </summary>
public static class LatexText
{
    /// <summary>
    /// Written at the start of a table's header row. A document theme (lilia-theme.sty) styles
    /// the header from it; the table's LaTeX is the same under every theme.
    /// </summary>
    public const string HeadRow = @"\liliaHeadRow ";

    /// <summary>
    /// Written before a table that has a header row: an empty \liliaHeadRow unless a theme defined
    /// one, so the table compiles anywhere it is copied. Empty rather than \relax, because a header
    /// cell may start with \multicolumn, which must come first in its cell.
    /// </summary>
    public const string HeadRowFallback = @"\providecommand{\liliaHeadRow}{}";

    /// <summary>
    /// Written beside <see cref="HeadRowFallback"/>: a pass-through \liliaTableHead unless a theme
    /// defined one, so a copied table compiles and prints exactly as before.
    /// </summary>
    public const string TableHeadFallback = @"\providecommand{\liliaTableHead}[1]{#1}";

    /// <summary>
    /// Written before a table with fewer than <see cref="BandMinBodyRows"/> body rows: a Banded
    /// paper leaves it unstriped (stripes on two or three rows are noise). Empty without a theme.
    /// The same for every paper, since it depends only on the table.
    /// </summary>
    public const string FewRows = @"\providecommand{\liliaFewRows}{}\liliaFewRows";

    /// <summary>Banded tables need at least this many body rows.</summary>
    public const int BandMinBodyRows = 4;

    /// <summary>
    /// A header cell's content in the theme's header wrapper, its \textbf kept: the theme sets
    /// the face (and, for a header band, the ink) per cell, and without a theme it is the cell.
    /// </summary>
    public static string TableHead(string cell) => @"\liliaTableHead{" + cell + "}";

    /// <summary>Escape every LaTeX-special character. The result is literal text.</summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text) AppendEscaped(sb, c);
        return sb.ToString();
    }

    /// <summary>
    /// Escape a table cell, preserving the small amount of markup the editor
    /// treats as authored rather than typed: <c>\textbf{…}</c> and <c>$…$</c>.
    ///
    /// <para>Cells were previously escaped wholesale, so a cell reading
    /// <c>\textbf{Ours}</c> — which the table tool documents, offers in its
    /// sample, and renders as bold in its own preview — was emitted as literal
    /// text. The client and the server disagreed about what a cell *is*, which
    /// meant the LaTeX shown to the author and the LaTeX we compiled and stored
    /// were different documents.</para>
    ///
    /// <para>The recognised set matches the client's renderer exactly. Widening
    /// it on one side only would recreate the divergence.</para>
    /// </summary>
    /// <summary>
    /// Whether the whole cell is one <c>\textbf{…}</c> — not merely whether it
    /// contains one.
    /// </summary>
    /// <remarks>
    /// Used by the table renderer so a header the author already bolded is not
    /// bolded a second time. Walks the braces rather than matching on the last
    /// character, because <c>\textbf{a} and \textbf{b}</c> also starts with the
    /// command and ends with a brace, and is not wholly bold.
    /// </remarks>
    public static bool IsWhollyBold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        const string Bold = "\\textbf{";
        var t = text.Trim();
        if (!t.StartsWith(Bold, StringComparison.Ordinal) || !t.EndsWith("}", StringComparison.Ordinal))
            return false;

        var depth = 0;
        for (var i = Bold.Length - 1; i < t.Length; i++)
        {
            if (t[i] == '\\') { i++; continue; }          // an escaped brace is literal
            if (t[i] == '{') depth++;
            else if (t[i] == '}')
            {
                depth--;
                // The command's own brace closed before the end, so whatever
                // follows is outside it.
                if (depth == 0) return i == t.Length - 1;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the whole cell is a single <c>$…$</c> maths run.
    /// </summary>
    /// <remarks>
    /// <c>\textbf</c> switches the TEXT font, and content inside <c>$…$</c> is
    /// typeset in math mode and does not inherit it — so <c>\textbf{$\Delta$}</c>
    /// changes no glyph. Bolding maths needs <c>\bm</c> or <c>\boldmath</c>,
    /// which is a different decision from "headers are bold" and not one to make
    /// on an author's behalf. So the table renderer leaves these unwrapped
    /// rather than emitting a wrapper that does nothing.
    /// </remarks>
    public static bool IsWhollyMaths(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var t = text.Trim();
        if (t.Length < 2 || t[0] != '$' || t[^1] != '$') return false;

        // Exactly one run: the opening $ must close at the very end.
        for (var i = 1; i < t.Length - 1; i++)
        {
            if (t[i] == '\\') { i++; continue; }   // an escaped dollar is literal
            if (t[i] == '$') return false;
        }
        return true;
    }

    public static string EscapeCell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        const string Bold = "\\textbf{";
        var sb = new StringBuilder(text.Length + 8);
        var i = 0;

        while (i < text.Length)
        {
            // \textbf{…} — the command survives; its contents are still user text, read the
            // same way (so maths inside bold stays maths). The argument runs to ITS closing
            // brace, not the first one: \textbf{a{b}c} is one bold run.
            if (string.CompareOrdinal(text, i, Bold, 0, Bold.Length) == 0)
            {
                var close = ClosingBrace(text, i + Bold.Length - 1);
                if (close >= 0)
                {
                    sb.Append(Bold)
                      .Append(EscapeCell(text[(i + Bold.Length)..close]))
                      .Append('}');
                    i = close + 1;
                    continue;
                }
            }

            // $…$ — math is passed through untouched. Escaping inside it would
            // defeat the point, and an invalid expression is caught by verification
            // rather than silently rewritten here. A run that could leave maths — break
            // the table's row or cell, define or read anything — is not maths: its $ is
            // printed as a dollar and the rest escaped (see IsInlineMath).
            if (text[i] == '$')
            {
                var close = text.IndexOf('$', i + 1);
                if (close >= 0 && IsInlineMath(text[(i + 1)..close]))
                {
                    sb.Append(text, i, close - i + 1);
                    i = close + 1;
                    continue;
                }
            }

            AppendEscaped(sb, text[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// A header cell as the table emitters write it: escaped as a cell, then bolded once — not
    /// around a cell the author already bolded, and not around maths, which <c>\textbf</c> does
    /// not reach. Shared by the preview's emitter and the export's, so the two print one header.
    /// </summary>
    public static string HeaderCell(string? text)
    {
        var escaped = EscapeCell(text);
        return IsWhollyBold(escaped) || IsWhollyMaths(escaped)
            ? escaped
            : $@"\textbf{{{escaped}}}";
    }

    // Control words a maths run in a table cell has no use for, and which could take the source
    // out of maths: environments, definitions, file and terminal I/O, category codes, building a
    // command name, and the alignment's own row and rule commands. TexSourceGuard still scans
    // the whole document; this keeps a cell from being a way to write LaTeX that is not maths.
    private static readonly HashSet<string> NotInCellMaths = new(StringComparer.Ordinal)
    {
        "begin", "end", "input", "include", "InputIfFileExists", "def", "gdef", "edef", "xdef", "let",
        "futurelet", "newcommand", "renewcommand", "providecommand", "DeclareRobustCommand",
        "catcode", "csname", "endcsname", "expandafter", "afterassignment", "aftergroup",
        "write", "read", "readline", "openin", "openout", "closein", "closeout", "immediate",
        "special", "directlua", "latelua", "luaexec", "scantokens", "usepackage", "RequirePackage",
        "makeatletter", "makeatother", "everypar", "everymath", "everydisplay", "everycr", "output",
        "cr", "crcr", "noalign", "omit", "span", "hline", "cline", "multicolumn", "tabularnewline",
        "newline", "par", "uppercase", "lowercase", "jobname", "string", "meaning", "detokenize",
    };

    /// <summary>
    /// Whether the text between two <c>$</c> in a cell is maths that stays maths: braces
    /// balanced, no <c>&amp;</c>, <c>#</c>, <c>%</c> or <c>\\</c> (a cell, a parameter, a comment
    /// that eats the row's end, a row break), and none of <see cref="NotInCellMaths"/>.
    /// </summary>
    public static bool IsInlineMath(string inner)
    {
        if (string.IsNullOrWhiteSpace(inner)) return false;
        var depth = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            var ch = inner[i];
            if (ch == '\\')
            {
                if (i + 1 >= inner.Length) return false;
                var next = inner[i + 1];
                if (char.IsLetter(next))
                {
                    var j = i + 1;
                    while (j < inner.Length && char.IsLetter(inner[j])) j++;
                    if (NotInCellMaths.Contains(inner[(i + 1)..j])) return false;
                    i = j - 1;
                    continue;
                }
                if (next == '\\') return false;     // a row break
                i++;                                // \{ \} \, \; \! \% \& \#: one symbol
                continue;
            }
            if (ch is '&' or '#' or '%') return false;
            if (ch == '{') depth++;
            else if (ch == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    /// <summary>The index of the brace closing the one at <paramref name="open"/>, or -1.</summary>
    private static int ClosingBrace(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }      // an escaped brace is literal
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static void AppendEscaped(StringBuilder sb, char c)
    {
        switch (c)
        {
            case '\\': sb.Append("\\textbackslash{}"); break;
            case '{': sb.Append("\\{"); break;
            case '}': sb.Append("\\}"); break;
            case '$': sb.Append("\\$"); break;
            case '&': sb.Append("\\&"); break;
            case '#': sb.Append("\\#"); break;
            case '_': sb.Append("\\_"); break;
            case '%': sb.Append("\\%"); break;
            case '^': sb.Append("\\textasciicircum{}"); break;
            case '~': sb.Append("\\textasciitilde{}"); break;
            default: sb.Append(c); break;
        }
    }
}
