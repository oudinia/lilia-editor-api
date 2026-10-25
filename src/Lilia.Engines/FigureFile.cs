using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// The file a figure's image is written as in an exported project (<c>figures/&lt;name&gt;</c>), and how
/// both emitters include it: under <c>\IfFileExists</c>, so a missing image prints a labelled frame
/// instead of stopping the compile. The check named the file differently and included it bare, so
/// it failed on a missing image that the export printed as a frame (MCP-111, run 3b).
/// </summary>
public static class FigureFile
{
    /// <summary>The file name under figures/: the URL's last segment, made safe for LaTeX.</summary>
    public static string Name(string? src, int index = 0)
    {
        if (string.IsNullOrEmpty(src)) return $"image-{index}.png";
        var parts = src.Split('/');
        var filename = parts.Length > 0 ? parts[^1] : $"image-{index}";
        filename = filename.Split('?')[0].Split('#')[0];
        if (!Regex.IsMatch(filename, @"\.(jpg|jpeg|png|gif|webp|svg|bmp|tiff?|pdf|eps)$", RegexOptions.IgnoreCase))
            filename = $"image-{index}.png";
        filename = Regex.Replace(filename, @"\s+", "-");
        filename = Regex.Replace(filename, @"[^a-zA-Z0-9._-]", "-");
        filename = Regex.Replace(filename, @"-+", "-");
        return filename.ToLowerInvariant();
    }

    /// <summary>
    /// Every image the emitter includes, in order: a figure's own, or, when it has subfigures, theirs
    /// instead (RenderService prints those and not the figure's src).
    /// </summary>
    public static IEnumerable<(Block Block, string Src)> Sources(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks.Where(b => b.Type == "figure"))
        {
            var content = block.Content.RootElement;
            if (content.ValueKind != JsonValueKind.Object) continue;
            if (content.TryGetProperty("subfigures", out var subs) && subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0)
            {
                foreach (var sub in subs.EnumerateArray())
                    if (sub.ValueKind == JsonValueKind.Object && sub.TryGetProperty("src", out var ss) && ss.GetString() is { Length: > 0 } subSrc)
                        yield return (block, subSrc);
            }
            else if (content.TryGetProperty("src", out var s) && s.GetString() is { Length: > 0 } src)
                yield return (block, src);
        }
    }

    /// <summary>The image, or a framed placeholder naming the missing file.</summary>
    public static string Include(string filename, string width, string placeholderWidth = @"0.7\textwidth") =>
        $@"\IfFileExists{{figures/{filename}}}{{%" + "\n"
        + $@"  \includegraphics[width={width}]{{figures/{filename}}}%" + "\n"
        // No \centering in the frame: the figure's own alignment (left, right) places it.
        + $@"}}{{\fbox{{\parbox{{{placeholderWidth}}}{{\small\textit{{[Missing figure: {LatexText.Escape(filename)}]}}}}}}}}";
}
