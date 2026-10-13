using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Blocks;

namespace Lilia.Engines;

/// <summary>
/// An equation block as LaTeX, for both emitters. The export's renderer and the check's had grown
/// apart (run 2, MCP-76): the export honoured the editor's mode (gather, align, display*) and put
/// a label inside an imported environment; the check gave split a host equation. This is the
/// union of the two, and the only one.
/// </summary>
public static class EquationLatex
{
    // Display environments that cannot sit inside equation / \[…\] — equation itself included, when
    // the source is a whole one. split, aligned, cases and friends are absent: they need the wrapper.
    private static readonly Regex StandaloneMathEnvironment = new(
        @"^\\begin\{(equation|align|gather|multline|flalign|alignat|eqnarray)(\*?)\}([\s\S]*)\\end\{\1\2\}$",
        RegexOptions.Compiled);

    public static string Render(JsonElement content)
    {
        var latex = EquationContent.ReadSource(content);
        // MathLive leaves placeholders in an equation it was editing.
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");
        if (string.IsNullOrWhiteSpace(latex)) return "% Empty equation";

        var (mode, numbered) = EquationContent.ReadMode(content);
        var label = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("label", out var lbl)
            && lbl.ValueKind == JsonValueKind.String ? lbl.GetString() ?? "" : "";
        // Not when the source has its own \label: two in one equation stop the compile.
        var labelPart = numbered && label.Length > 0 && !latex.Contains(@"\label{", StringComparison.Ordinal)
            ? $@"\label{{{LabelKey.Effective("equation", label)}}}" : "";

        // A split, or an environment that needs its own paragraph, is never inline maths.
        if (mode == "inline" && (latex.Contains(@"\begin{split}", StringComparison.Ordinal) || StandaloneMathEnvironment.IsMatch(latex.Trim())))
        {
            // Numbered like any display, unless the block says otherwise.
            mode = "display";
            numbered = !(content.ValueKind == JsonValueKind.Object && content.TryGetProperty("numbered", out var n) && n.ValueKind == JsonValueKind.False);
            labelPart = numbered && label.Length > 0 && !latex.Contains(@"\label{", StringComparison.Ordinal)
                ? $@"\label{{{LabelKey.Effective("equation", label)}}}" : "";
        }

        // The source is itself a display environment (what a .tex import stores for align /
        // gather / multline). Wrapped in equation or \[…\] it is fatal; numbering is the block's.
        if (mode != "inline" && TryRenderStandalone(latex, numbered, labelPart) is { } standalone)
            return standalone;

        // An alignment body (a &= b \\ c &= d) in any other mode: its & is fatal outside align.
        if (mode is not ("align" or "gather" or "inline") && EquationContent.HasTopLevelAlignment(latex))
            mode = "align";

        return mode switch
        {
            "inline" => $"${latex}$",
            "align" or "gather" => $@"\begin{{{mode}{(numbered ? "" : "*")}}}{labelPart}" + "\n" + latex + "\n" + $@"\end{{{mode}{(numbered ? "" : "*")}}}",
            _ when numbered => $@"\begin{{equation}}{labelPart}" + "\n" + latex + "\n" + @"\end{equation}",
            _ => @"\begin{equation*}" + "\n" + latex + "\n" + @"\end{equation*}",
        };
    }

    public static string? TryRenderStandalone(string latex, bool numbered, string labelPart)
    {
        var m = StandaloneMathEnvironment.Match(latex.Trim());
        if (!m.Success) return null;

        var env = m.Groups[1].Value + (numbered ? "" : "*");
        var body = m.Groups[3].Value;
        var arg = "";
        var argMatch = Regex.Match(body, @"^\{[^{}]*\}");
        if (m.Groups[1].Value == "alignat" && argMatch.Success)
        {
            arg = argMatch.Value;
            body = body[argMatch.Length..];
        }
        return $@"\begin{{{env}}}{arg}" + (labelPart.Length > 0 ? "\n" + labelPart : "") + "\n" + body.Trim('\n', '\r', ' ') + "\n" + $@"\end{{{env}}}";
    }
}
