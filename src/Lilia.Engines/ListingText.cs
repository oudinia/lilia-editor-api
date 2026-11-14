using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Code that listings can print. Under pdflatex listings reads bytes, so any non-ASCII
/// character — even the é of "Café" — failed the whole document (MCP-18). Each run of them goes
/// through an escape, where LaTeX's own UTF-8 handling (and Lilia's Unicode shims) print it.
///
/// <para>The line <c>\end{lstlisting}</c> in the code (a LaTeX tutorial showing how to write a
/// listing) ended the listing there, and the rest of the code was compiled as LaTeX (MCP-129,
/// retested 14 Nov: "Undefined control sequence"). It is written with an empty escape inside,
/// <c>\end{lst(*@@*)listing}</c>: listings never sees its end marker, and the escape prints nothing.</para>
/// </summary>
public static class ListingText
{
    private const string EndMarker = @"\end{lstlisting}";

    private static readonly Regex NonAscii = new(@"[^\x00-\x7F]+", RegexOptions.Compiled);

    // Delimiters the code itself must not contain; the first free pair is used.
    private static readonly (string Open, string Close)[] Delimiters = [("(*@", "@*)"), ("(*|", "|*)"), ("(@@", "@@)")];

    /// <summary>The code with its non-ASCII runs escaped, and the listings option that enables it (null: none needed).</summary>
    public static (string Code, string? Option) Escape(string code)
    {
        if (string.IsNullOrEmpty(code)) return (code, null);
        var nonAscii = NonAscii.IsMatch(code);
        var ends = code.Contains(EndMarker, StringComparison.Ordinal);
        if (!nonAscii && !ends) return (code, null);
        foreach (var (open, close) in Delimiters)
        {
            if (code.Contains(open, StringComparison.Ordinal) || code.Contains(close, StringComparison.Ordinal)) continue;
            var escaped = nonAscii ? NonAscii.Replace(code, m => open + m.Value + close) : code;
            if (ends) escaped = escaped.Replace(EndMarker, @"\end{lst" + open + close + "listing}", StringComparison.Ordinal);
            return (escaped, $"escapeinside={{{open}}}{{{close}}}");
        }
        return (code, null);
    }
}
