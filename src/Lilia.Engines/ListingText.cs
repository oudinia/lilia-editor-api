using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Code that listings can print. Under pdflatex listings reads bytes, so any non-ASCII
/// character — even the é of "Café" — failed the whole document (MCP-18). Each run of them goes
/// through an escape, where LaTeX's own UTF-8 handling (and Lilia's Unicode shims) print it.
/// </summary>
public static class ListingText
{
    private static readonly Regex NonAscii = new(@"[^\x00-\x7F]+", RegexOptions.Compiled);

    // Delimiters the code itself must not contain; the first free pair is used.
    private static readonly (string Open, string Close)[] Delimiters = [("(*@", "@*)"), ("(*|", "|*)"), ("(@@", "@@)")];

    /// <summary>The code with its non-ASCII runs escaped, and the listings option that enables it (null: none needed).</summary>
    public static (string Code, string? Option) Escape(string code)
    {
        if (string.IsNullOrEmpty(code) || !NonAscii.IsMatch(code)) return (code, null);
        foreach (var (open, close) in Delimiters)
        {
            if (code.Contains(open, StringComparison.Ordinal) || code.Contains(close, StringComparison.Ordinal)) continue;
            return (NonAscii.Replace(code, m => open + m.Value + close), $"escapeinside={{{open}}}{{{close}}}");
        }
        return (code, null);
    }
}
