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

    // A label without a prefix after a word that names what is referred to: Equation~(loss),
    // Theorem~(main). It was left as typed (MCP-106, run 3b); "approx~(3 items)" still is.
    public static readonly Regex KindParenBare = new(
        @"\b(?<kind>Equations?|Eqs?\.?|Theorems?|Lemmas?|Propositions?|Corollar(?:y|ies)|Definitions?|Remarks?|Examples?|Claims?|Conjectures?|Assumptions?|Figures?|Figs?\.?|Tables?|Sections?|Chapters?|Appendi(?:x|ces)|Algorithms?|Listings?|Parts?)~\((?<key>[A-Za-z][A-Za-z0-9_.-]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The square-bracket form (@ref[x], @cite[x]) is the old spec's, still in many prompts (run 2, R1):
    // read like the braced one. A bibliography block's "@cite[key] Author" item is not prose.
    public static readonly Regex AtRef = new(@"@ref(?:\{(?<key>[^{}]+)\}|\[(?<key>[^\]\s]+)\])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly Regex AtCite = new(@"@cite(?:\{(?<keys>[^{}]+)\}|\[(?<keys>[^\]]+)\])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>\eqref</c> for an equation label (<c>eq:…</c>), <c>\ref</c> for anything else.</summary>
    public static string RefCommand(string label)
    {
        var key = LabelKey.Safe(label);
        return key.StartsWith("eq:", StringComparison.OrdinalIgnoreCase) ? $"\\eqref{{{key}}}" : $"\\ref{{{key}}}";
    }

    private static readonly Regex LabelDef = new(@"\\label\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex RefUse = new(@"\\(?<cmd>eqref|ref|cref|Cref|autoref|pageref|nameref)\*?\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly string[] BarePrefixes = ["eq", "fig", "tbl", "tab", "alg", "thm", "lem", "sec", "lst"];

    /// <summary>
    /// A reference to a bare name that the document only defines with its kind's prefix — an
    /// equation labelled "loss" is \label{eq:loss} (<see cref="LabelKey.Effective"/>) — points at
    /// that label. The author's own \eqref{loss} printed "??" (MCP-106).
    /// </summary>
    public static string ResolveBareReferences(string latex)
    {
        if (string.IsNullOrEmpty(latex) || latex.IndexOf("ref{", StringComparison.Ordinal) < 0) return latex;
        var defined = LabelDef.Matches(latex).Select(m => m.Groups["k"].Value.Trim()).ToHashSet(StringComparer.Ordinal);
        return RefUse.Replace(latex, m =>
        {
            var keys = m.Groups["k"].Value.Split(',');
            var changed = false;
            for (var i = 0; i < keys.Length; i++)
            {
                var k = keys[i].Trim();
                if (k.Length == 0 || k.Contains(':') || defined.Contains(k)) continue;
                var found = BarePrefixes.Select(p => $"{p}:{k}").Where(defined.Contains).ToList();
                if (found.Count != 1) continue;
                keys[i] = found[0];
                changed = true;
            }
            return changed ? m.Value.Replace(m.Groups["k"].Value, string.Join(",", keys)) : m.Value;
        });
    }

    private static string CiteKeys(string keys) =>
        string.Join(",", keys.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0));

    /// <summary>Rewrite all three forms, each finished piece handed to <paramref name="protect"/>.</summary>
    public static string Resolve(string text, Func<string, string> protect)
    {
        if (string.IsNullOrEmpty(text) || (text.IndexOf('@') < 0 && text.IndexOf('~') < 0)) return text;
        var result = KindParen.Replace(text, m => m.Groups["kind"].Value + protect("~" + RefCommand(m.Groups["key"].Value)));
        result = KindParenBare.Replace(result, m =>
        {
            var key = LabelKey.Safe(m.Groups["key"].Value);
            var equation = m.Groups["kind"].Value.StartsWith("Eq", StringComparison.Ordinal);
            return m.Groups["kind"].Value + protect(equation ? $"~\\eqref{{{key}}}" : $"~\\ref{{{key}}}");
        });
        // @ref is a plain \ref whatever the label, as the .tex export always made it.
        result = AtRef.Replace(result, m => protect($"\\ref{{{LabelKey.Safe(m.Groups["key"].Value)}}}"));
        result = AtCite.Replace(result, m => protect($"\\cite{{{CiteKeys(m.Groups["keys"].Value)}}}"));
        return result;
    }
}
