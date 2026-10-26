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

    /// <summary>
    /// The names a submission gives the figures' files: <c>fig1.png</c>, <c>fig2a.pdf</c> (a subfigure), by the
    /// number the figure prints with, as publishers ask (Elsevier, IEEE, Springer: "name each file by its
    /// figure number"). Keyed by <see cref="Name"/>, the name a draft export uses. Every figure block counts,
    /// with or without an image, as LaTeX numbers them; an image used twice keeps its first name.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SubmissionNames(IEnumerable<Block> blocks)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var block in blocks.Where(b => b.Type == "figure"))
        {
            n++;
            var content = block.Content.RootElement;
            if (content.ValueKind != JsonValueKind.Object) continue;
            var subs = content.TryGetProperty("subfigures", out var ss) && ss.ValueKind == JsonValueKind.Array && ss.GetArrayLength() > 0
                ? ss.EnumerateArray().Select(sub => sub.ValueKind == JsonValueKind.Object && sub.TryGetProperty("src", out var x) ? x.GetString() : null).ToList()
                : null;
            if (subs is not null)
            {
                for (var i = 0; i < subs.Count; i++)
                    if (subs[i] is { Length: > 0 } src) names.TryAdd(Name(src), $"fig{n}{Letter(i)}{Extension(Name(src))}");
            }
            else if (content.TryGetProperty("src", out var s) && s.GetString() is { Length: > 0 } src)
                names.TryAdd(Name(src), $"fig{n}{Extension(Name(src))}");
        }
        return names;
    }

    // a … z, then aa, ab … (LaTeX's subfigure letters stop at z; a file name need not).
    private static string Letter(int i) => i < 26 ? ((char)('a' + i)).ToString() : Letter(i / 26 - 1) + (char)('a' + i % 26);

    private static string Extension(string name) => Path.GetExtension(name) is { Length: > 0 } e ? e : ".png";

    /// <summary>
    /// The LaTeX with each figure file's draft name replaced by its submission name, in one pass (a draft
    /// image already called fig1.png is not renamed twice).
    /// </summary>
    public static string Rename(string latex, IReadOnlyDictionary<string, string> names)
    {
        if (names.Count == 0) return latex;
        latex = Regex.Replace(latex, @"\{figures/([^{}]+)\}",
            m => names.TryGetValue(m.Groups[1].Value, out var to) ? $"{{figures/{to}}}" : m.Value);
        // And the frame that would name a missing one (Include).
        var printed = names.ToDictionary(n => LatexText.Escape(n.Key), n => LatexText.Escape(n.Value), StringComparer.Ordinal);
        return Regex.Replace(latex, @"\[Missing figure: ([^\]]+)\]",
            m => printed.TryGetValue(m.Groups[1].Value, out var to) ? $"[Missing figure: {to}]" : m.Value);
    }

    /// <summary>The image, or a framed placeholder naming the missing file.</summary>
    public static string Include(string filename, string width, string placeholderWidth = @"0.7\textwidth") =>
        $@"\IfFileExists{{figures/{filename}}}{{%" + "\n"
        + $@"  \includegraphics[width={width}]{{figures/{filename}}}%" + "\n"
        // No \centering in the frame: the figure's own alignment (left, right) places it.
        + $@"}}{{\fbox{{\parbox{{{placeholderWidth}}}{{\small\textit{{[Missing figure: {LatexText.Escape(filename)}]}}}}}}}}";
}
