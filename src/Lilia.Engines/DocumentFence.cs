using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// <c>\begin{document}</c> / <c>\end{document}</c> typed in a block's text (MCP-52, 12 Oct): emitted
/// raw, <c>\end{document}</c> silently cut the PDF there, with a clean check. Lilia writes the
/// document's frame itself, so in text they are printed as typed.
/// </summary>
public static class DocumentFence
{
    public static readonly Regex Pattern = new(@"\\(?<which>begin|end)\s*\{document\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The printable form, as finished LaTeX.</summary>
    public static string Printable(Match m) => $@"\textbackslash{{}}{m.Groups["which"].Value}\{{document\}}";

    public static string Neutralize(string text, Func<string, string> protect) =>
        string.IsNullOrEmpty(text) || text.IndexOf("document", StringComparison.Ordinal) < 0
            ? text
            : Pattern.Replace(text, m => protect(Printable(m)));
}
