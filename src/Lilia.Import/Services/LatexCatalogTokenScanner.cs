using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// Post-parse pass that walks the raw LaTeX source once and collects
/// every command / environment it sees, with per-token counts. Feeds
/// the LaTeX catalog (Phase 2) — ILatexCatalogService then attributes
/// each token to its package and records usage against the review
/// session so we get observability on what users throw at us.
/// </summary>
public static class LatexCatalogTokenScanner
{
    // \cmd (optional star) — captures the bare name without backslash.
    // Excludes LaTeX-internal tokens like \@, \%, \{, \}, \\ (single char).
    private static readonly Regex CommandRegex = new(@"\\([a-zA-Z]+\*?)", RegexOptions.Compiled);

    // \begin{env} and \end{env} — we only count \begin so each env = 1.
    private static readonly Regex BeginEnvRegex = new(@"\\begin\s*\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Walk the source once, return a dictionary keyed by
    /// (name, kind) with hit counts. Kind is "command" or "environment".
    /// Caller pairs with a catalog lookup to resolve package attribution.
    /// </summary>
    public static Dictionary<(string Name, string Kind), int> Scan(string latexSource)
    {
        var counts = new Dictionary<(string, string), int>();

        foreach (Match m in BeginEnvRegex.Matches(latexSource))
        {
            var env = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(env)) continue;
            var key = (env, "environment");
            counts[key] = counts.TryGetValue(key, out var v) ? v + 1 : 1;
        }

        foreach (Match m in CommandRegex.Matches(latexSource))
        {
            var name = m.Groups[1].Value;
            // Skip begin/end — handled as environments above.
            if (name == "begin" || name == "end") continue;
            var key = (name, "command");
            counts[key] = counts.TryGetValue(key, out var v) ? v + 1 : 1;
        }

        return counts;
    }
}
