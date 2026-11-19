using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines.Publishers;

/// <summary>
/// acmart's teaser: the figure under the title, which the class prints only when its teaserfigure comes before
/// <c>\maketitle</c>. An imported ACM paper's teaser landed in the body, after \maketitle, and never printed, and its
/// image was not kept (launch-bot #242, 19 Nov 2026). Under another class it is a full-width figure (span "page").
/// </summary>
public static class TeaserFigure
{
    /// <summary>A figure block marked as the teaser (content "teaser": true).</summary>
    public static bool Is(Block block)
    {
        if (block.Type is not ("figure" or "image") || block.Content is null) return false;
        var c = block.Content.RootElement;
        return c.ValueKind == JsonValueKind.Object && c.TryGetProperty("teaser", out var t) && t.ValueKind == JsonValueKind.True;
    }

    /// <summary>The teaser that goes before \maketitle: acmart only, the first marked figure.</summary>
    public static Block? For(string? className, IEnumerable<Block> blocks) =>
        PublisherClasses.Canonical(className) == "acmart" ? blocks.FirstOrDefault(Is) : null;

    private static readonly Regex Begin = new(@"\\begin\{figure\*?\}(?:\[[^\]]*\])?", RegexOptions.Compiled);
    private static readonly Regex End = new(@"\\end\{figure\*?\}", RegexOptions.Compiled);

    /// <summary>The figure's LaTeX as a teaserfigure (no placement: it isn't a float).</summary>
    public static string AsTeaser(string figureLatex) =>
        End.Replace(Begin.Replace(figureLatex, @"\begin{teaserfigure}", 1), @"\end{teaserfigure}", 1);

    /// <summary>The teaser written on the line before the front matter's \maketitle (or at its end when it has none).</summary>
    public static string BeforeMaketitle(string frontBody, string teaserLatex)
    {
        var at = frontBody.IndexOf(@"\maketitle", StringComparison.Ordinal);
        var block = teaserLatex.TrimEnd() + "\n";
        return at < 0 ? frontBody.TrimEnd() + "\n" + block : frontBody[..at] + block + frontBody[at..];
    }
}
