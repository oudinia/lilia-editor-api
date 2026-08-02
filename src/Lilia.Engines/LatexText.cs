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
