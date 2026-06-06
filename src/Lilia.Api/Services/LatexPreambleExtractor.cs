using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// Pulls the reusable preamble out of a raw LaTeX source so an import can
/// preserve it onto the finalized document: the document class, the
/// <c>\usepackage</c> set, and the author's custom macros / environments.
///
/// Focused regexes over the preamble region only (everything before
/// <c>\begin{document}</c>) — not a full parser. Brace matching is one level
/// deep (mirrors the strip regex the body parser already uses), which covers
/// the overwhelming majority of real macro bodies; a pathological deeply-nested
/// body simply isn't captured rather than capturing wrong.
/// </summary>
public static class LatexPreambleExtractor
{
    public record Result(string? DocumentClass, string? PackagesJson, string? CustomPreamble);

    // One-level-nested brace group, e.g. {a {b} c}. Reused across macro patterns.
    private const string Braced = @"\{(?:[^{}]|\{[^{}]*\})*\}";

    private static readonly Regex DocClassRe =
        new(@"\\documentclass\s*(?:\[[^\]]*\])?\s*\{([^}]+)\}", RegexOptions.Compiled);

    private static readonly Regex UsePackageRe =
        new(@"\\usepackage\s*(?:\[([^\]]*)\])?\s*\{([^}]+)\}", RegexOptions.Compiled);

    // Capture the WHOLE macro definition verbatim, in source order.
    private static readonly Regex[] MacroRes =
    {
        // \newcommand / \renewcommand / \providecommand {\name}[n][default]{body}
        new(@"\\(?:re|provide)?newcommand\*?\s*\{?\\[A-Za-z@]+\}?\s*(?:\[[^\]]*\])*\s*" + Braced, RegexOptions.Compiled),
        // \newenvironment / \renewenvironment {name}[n][default]{begin}{end}
        new(@"\\(?:re)?newenvironment\*?\s*\{[^}]*\}\s*(?:\[[^\]]*\])*\s*" + Braced + @"\s*" + Braced, RegexOptions.Compiled),
        // \DeclareMathOperator{\name}{text}
        new(@"\\DeclareMathOperator\*?\s*\{[^}]*\}\s*\{[^}]*\}", RegexOptions.Compiled),
        // \def\name{body}  (TeX primitive form, no args captured beyond the body)
        new(@"\\def\s*\\[A-Za-z@]+\s*" + Braced, RegexOptions.Compiled),
    };

    public static Result Extract(string? rawLatex)
    {
        if (string.IsNullOrWhiteSpace(rawLatex)) return new Result(null, null, null);

        // Preamble only — everything before \begin{document}.
        var beginIdx = rawLatex.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var preamble = beginIdx >= 0 ? rawLatex[..beginIdx] : rawLatex;

        // Document class (name only; options/columns are handled elsewhere).
        string? docClass = null;
        var dc = DocClassRe.Match(preamble);
        if (dc.Success)
        {
            var c = dc.Groups[1].Value.Trim();
            if (c.Length > 0) docClass = c;
        }

        // Packages → JSON array of { name, options? } (matches Document.LatexPackages).
        var pkgs = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in UsePackageRe.Matches(preamble))
        {
            var opts = m.Groups[1].Success ? m.Groups[1].Value.Trim() : "";
            foreach (var rawName in m.Groups[2].Value.Split(','))
            {
                var name = rawName.Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;
                pkgs.Add(string.IsNullOrEmpty(opts)
                    ? new Dictionary<string, string> { ["name"] = name }
                    : new Dictionary<string, string> { ["name"] = name, ["options"] = opts });
            }
        }
        string? packagesJson = pkgs.Count > 0 ? JsonSerializer.Serialize(pkgs) : null;

        // Macros / environments — collect each match with its position so the
        // output preserves source order across the different definition kinds.
        var hits = new List<(int Pos, string Text)>();
        foreach (var re in MacroRes)
            foreach (Match m in re.Matches(preamble))
                hits.Add((m.Index, m.Value.Trim()));
        hits.Sort((a, b) => a.Pos.CompareTo(b.Pos));

        string? customPreamble = null;
        if (hits.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var (_, text) in hits) sb.AppendLine(text);
            customPreamble = sb.ToString().TrimEnd();
        }

        return new Result(docClass, packagesJson, customPreamble);
    }
}
