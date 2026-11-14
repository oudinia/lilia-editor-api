using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// LaTeX with the bodies of its verbatim environments blanked: what the document's own commands
/// are, as opposed to text a code block only shows (MCP-127, MCP-128; retested 14 Nov). A LaTeX code
/// block showing <c>\usepackage{xeCJK}</c> switched the engine to XeLaTeX, and one showing
/// <c>\label{eq:smoke}</c> or <c>\ref{eq:missing}</c> made the check report a duplicate label and a
/// dangling reference in a document that prints correctly.
///
/// <para>Line by line, as LaTeX reads it: the body runs from the line after <c>\begin{env}</c> to the
/// first line holding <c>\end{env}</c>. The begin and end lines stay (a listing's label is in its
/// options); every body line becomes empty, so line numbers still point at the same blocks.</para>
/// </summary>
public static class LatexVerbatim
{
    private static readonly Regex Begin = new(
        @"\\begin\{(?<env>lstlisting|verbatim\*?|Verbatim\*?|BVerbatim|LVerbatim|minted|comment)\}",
        RegexOptions.Compiled);

    public static string Blank(string? source)
    {
        if (string.IsNullOrEmpty(source) || !source.Contains(@"\begin{", StringComparison.Ordinal)) return source ?? "";
        var lines = source.Split('\n');
        string? end = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (end is not null)
            {
                if (lines[i].Contains(end, StringComparison.Ordinal)) end = null;
                else lines[i] = "";
                continue;
            }
            var m = Begin.Match(lines[i]);
            if (!m.Success) continue;
            var close = $@"\end{{{m.Groups["env"].Value}}}";
            // A one-line environment closes on its own line.
            if (lines[i].IndexOf(close, m.Index + m.Length, StringComparison.Ordinal) < 0) end = close;
        }
        return string.Join('\n', lines);
    }
}
