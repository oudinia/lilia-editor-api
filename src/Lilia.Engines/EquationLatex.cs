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

    /// <summary>What an equation with unbalanced braces prints instead of itself.</summary>
    public const string UnbalancedNote = "This equation has an unclosed or extra brace, so it is left out of the PDF: fix it in the editor.";

    /// <summary>True when every unescaped { has its }, and no } closes nothing (\{ and \} are text).</summary>
    public static bool BracesBalance(string latex)
    {
        var depth = 0;
        for (var i = 0; i < latex.Length; i++)
        {
            var c = latex[i];
            if (c == '\\') { i++; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    public static string Render(JsonElement content)
    {
        var latex = EquationContent.ReadMathSource(content);
        // MathLive leaves placeholders in an equation it was editing.
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");
        if (string.IsNullOrWhiteSpace(latex)) return "% Empty equation";

        // One equation with an unclosed brace stopped the whole LaTeX PDF ("File ended while scanning use of \frac"; the
        // document sweep, E-025, 16 Nov 2026): every other block was lost with it. It now prints as a red note in its place;
        // the rest of the document compiles, and the document check still points at the block.
        if (!BracesBalance(latex))
        {
            var (m, _) = EquationContent.ReadMode(content);
            return m == "inline" ? $@"\textcolor{{red}}{{{UnbalancedNote}}}"
                : @"\begin{center}\fbox{\parbox{0.9\linewidth}{\centering\small\textcolor{red}{" + UnbalancedNote + @"}}}\end{center}";
        }

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
            "align" or "gather" => $@"\begin{{{mode}{(numbered ? "" : "*")}}}{labelPart}" + "\n" + (numbered ? latex : WithoutLabels(latex)) + "\n" + $@"\end{{{mode}{(numbered ? "" : "*")}}}",
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
        if (!numbered && env != "equation*") body = WithoutLabels(body);
        return $@"\begin{{{env}}}{arg}" + (labelPart.Length > 0 ? "\n" + labelPart : "") + "\n" + body.Trim('\n', '\r', ' ') + "\n" + $@"\end{{{env}}}";
    }

    /// <summary>The comment that stands for a label taken out of an unnumbered alignment (EmittedLatexAudit reads it).</summary>
    public const string DroppedLabelMark = "% lilia: unnumbered, label dropped: ";

    /// <summary>
    /// An unnumbered alignment (align*, gather*, multline*…) without its \label{…}s: amsmath never writes them, and one
    /// left over is taken by the next numbered equation, which then stops ("Multiple \label's: label … will be lost",
    /// the launch bot's #124 note, 17 Nov 2026). Each goes to a comment line, so the check still says a reference to it
    /// prints ??. In equation* a label takes the current number and stays.
    /// </summary>
    public static string WithoutLabels(string body)
    {
        var dropped = new List<string>();
        var kept = LabelCommand.Replace(body, m => { dropped.Add(m.Groups["k"].Value.Trim()); return ""; });
        return dropped.Count == 0 ? body : kept.TrimEnd() + "\n" + string.Join("\n", dropped.Select(k => DroppedLabelMark + k));
    }

    private static readonly Regex LabelCommand = new(@"\\label\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
}
