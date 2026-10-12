using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Lilia's own ways of pointing at things from text, turned into LaTeX (report of 12 Oct 2026: the
/// compile emitter printed them as typed, so a PDF read "Theorem~(thm:pyth)" or "@cite{euclid}").
///
/// <list type="bullet">
/// <item><c>Kind~(prefix:label)</c>: the form Lilia's help and AI skills teach
///   (<c>Equation~(eq:loss)</c>, <c>Figure~(fig:arch)</c>, <c>Theorem~(thm:main)</c>). Becomes
///   <c>Kind~\eqref{…}</c> for an equation (prints "Equation (3)") and <c>Kind~\ref{…}</c> otherwise.
///   Only a label with a prefix (<c>eq:</c>, <c>fig:</c>…) counts, so "approx~(3 items)" stays text.</item>
/// <item><c>@ref{label}</c>: what the editor's Cross-references panel inserts. Always <c>\ref</c>.</item>
/// <item><c>@cite{key}</c>: Lilia's citation shorthand (LML, AI skills).</item>
/// </list>
///
/// The caller passes <c>protect</c>, which stores a finished LaTeX piece and returns a placeholder,
/// so the escape passes that follow leave it alone (the <c>~</c> stays a tie, not
/// <c>\textasciitilde{}</c>).
/// </summary>
public static class CrossRefSyntax
{
    public static readonly Regex KindParen = new(
        @"(?<kind>\p{L}[\p{L}\p{M}]*)~\((?<key>[A-Za-z][A-Za-z0-9-]*:[^()\s{}]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly Regex AtRef = new(@"@ref\{(?<key>[^{}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly Regex AtCite = new(@"@cite\{(?<keys>[^{}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>\eqref</c> for an equation label (<c>eq:…</c>), <c>\ref</c> for anything else.</summary>
    public static string RefCommand(string label)
    {
        var key = LabelKey.Safe(label);
        return key.StartsWith("eq:", StringComparison.OrdinalIgnoreCase) ? $"\\eqref{{{key}}}" : $"\\ref{{{key}}}";
    }

    private static string CiteKeys(string keys) =>
        string.Join(",", keys.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0));

    /// <summary>Rewrite all three forms, each finished piece handed to <paramref name="protect"/>.</summary>
    public static string Resolve(string text, Func<string, string> protect)
    {
        if (string.IsNullOrEmpty(text) || (text.IndexOf('@') < 0 && text.IndexOf('~') < 0)) return text;
        var result = KindParen.Replace(text, m => m.Groups["kind"].Value + protect("~" + RefCommand(m.Groups["key"].Value)));
        // @ref is a plain \ref whatever the label, as the .tex export always made it.
        result = AtRef.Replace(result, m => protect($"\\ref{{{LabelKey.Safe(m.Groups["key"].Value)}}}"));
        result = AtCite.Replace(result, m => protect($"\\cite{{{CiteKeys(m.Groups["keys"].Value)}}}"));
        return result;
    }
}
