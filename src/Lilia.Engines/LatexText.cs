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
            // \textbf{…} — the command survives; its contents are still user text.
            if (string.CompareOrdinal(text, i, Bold, 0, Bold.Length) == 0)
            {
                var close = text.IndexOf('}', i + Bold.Length);
                if (close >= 0)
                {
                    sb.Append(Bold)
                      .Append(Escape(text[(i + Bold.Length)..close]))
                      .Append('}');
                    i = close + 1;
                    continue;
                }
            }

            // $…$ — math is passed through untouched. Escaping inside it would
            // defeat the point, and an unbalanced or invalid expression is caught
            // by verification rather than silently rewritten here.
            if (text[i] == '$')
            {
                var close = text.IndexOf('$', i + 1);
                if (close >= 0)
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
