using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// Mathpix's figures as Lilia's figure blocks (figures in imported scans, 20 Nov 2026). On the launch bot's handwritten
/// page the two sketches were lost and their ids printed as text ("2026_11_20_…-1"): Mathpix's tex.zip sets a figure
/// without a caption as a bare <c>\includegraphics[max width=\textwidth]{id}</c>, alone or in a <c>center</c>
/// environment, which the LaTeX import reads as a figure only inside a <c>figure</c> environment; and its Markdown can
/// put an image link in the middle of a line.
/// </summary>
public static partial class MathpixFigures
{
    /// <summary>
    /// Every <c>\includegraphics</c> outside a <c>figure</c> environment, with the <c>center</c> environment around it if
    /// any, set in a <c>figure</c> environment of its own (as a paragraph: blank lines around it).
    /// </summary>
    public static string AsFigureEnvironments(string tex)
    {
        if (string.IsNullOrEmpty(tex) || !tex.Contains(@"\includegraphics", StringComparison.Ordinal)) return tex;
        var figures = FigureEnvironment().Matches(tex).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
        bool InFigure(int at) => figures.Any(f => at >= f.Index && at < f.End);
        return BareGraphics().Replace(tex, m => InFigure(m.Index)
            ? m.Value
            : $"\n\n\\begin{{figure}}[h]\n\\centering\n{m.Groups["graphics"].Value}\n\\end{{figure}}\n\n");
    }

    /// <summary>Every Markdown image link on a line of its own, so it is read as a figure and never left in a paragraph.</summary>
    public static string ImagesOnTheirOwnLines(string markdown) =>
        string.IsNullOrEmpty(markdown) ? markdown : MarkdownImage().Replace(markdown, m => $"\n\n{m.Value}\n\n");

    /// <summary>A Mathpix figure id (<c>2026_11_20_0a1b2c…-1</c>) or a cropped image's address, which must never be text.</summary>
    public static bool HasRawReference(string text) => RawReference().IsMatch(text);

    /// <summary>The text with every Mathpix figure id and cropped-image address taken out.</summary>
    public static string WithoutRawReferences(string text) =>
        MultipleSpaces().Replace(RawReference().Replace(text, ""), " ").Trim();

    [GeneratedRegex(@"\\begin\{figure\*?\}.*?\\end\{figure\*?\}", RegexOptions.Singleline)]
    private static partial Regex FigureEnvironment();

    [GeneratedRegex(@"(?:\\begin\{center\}\s*)?(?<graphics>\\includegraphics\*?(?:\[[^\]]*\])?\{[^{}]+\})(?:\s*\\end\{center\})?")]
    private static partial Regex BareGraphics();

    [GeneratedRegex(@"!\[[^\]\n]*\]\([^)\s]+\)")]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"https?://cdn\.mathpix\.com/\S+|\b\d{4}_\d{2}_\d{2}_[0-9a-z]{6,}(?:-\d+)?(?:\.(?:jpe?g|png))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex RawReference();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultipleSpaces();
}
