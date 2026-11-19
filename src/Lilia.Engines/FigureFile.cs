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
    /// <summary>
    /// An image TeX Live itself provides (the mwe package's example-image, example-image-a/b/c, -golden, -16x9…), as a
    /// source names it: included as written, from TeX Live, the way Overleaf does. It became a missing figures/image-0.png
    /// and the author was asked to upload a file they never had (launch-bot #244, 19 Nov 2026). Null otherwise.
    /// </summary>
    public static string? TexLiveImage(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        var name = src.Trim();
        return TexLiveImageName.IsMatch(name) ? name : null;
    }

    private static readonly Regex TexLiveImageName = new(
        @"^example-(?:image|grid-100x100(?:bp|pt)|movie)(?:-(?:a|b|c|golden|plain|duck|1x1|10x16|16x10|16x9|3x4|4x3|9x16|a4|a5|letter|landscape|portrait|golden-upright|a4-numbered|letter-numbered)){0,2}(?:\.(?:pdf|png|jpg|eps))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// The image's name as the source wrote it (its last path segment), for messages to the author. <see cref="Name"/> is the
    /// file the export writes, and a name without an image extension becomes image-0.png there; the check then asked the
    /// author to upload "image-0.png", a file they never had, for \includegraphics{lilia-launch-no-such-image}
    /// (launch-bot #244, 19 Nov 2026).
    /// </summary>
    /// <remarks>
    /// Two sources have no name worth printing (Zayd's review of API #652): an image embedded in the block
    /// (<c>data:image/png;base64,…</c>), whose last segment is a run of base64, and one Lilia stored itself, whose file
    /// name is a GUID (AssetService: <c>{user}/documents/{doc}/images/{asset}.png</c>). They get a label instead,
    /// <see cref="EmbeddedLabel"/> or <see cref="UploadedLabel"/>; the figure's caption and place tell the author which.
    /// </remarks>
    public static string WrittenName(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return "(no file)";
        var s = src.Trim();
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return EmbeddedLabel;
        if (StorageKey(s) is { } key && UploadedKey.IsMatch(key)) return UploadedLabel;
        var last = s.Split('/')[^1].Split('?')[0].Split('#')[0];
        return last.Length > 0 ? last : s;
    }

    public const string EmbeddedLabel = "an embedded image";
    public const string UploadedLabel = "an uploaded image";

    /// <summary>"The image plot" for a named file, "An embedded image" for a label: the subject of a sentence about it.</summary>
    public static string Subject(string writtenName) => writtenName is EmbeddedLabel or UploadedLabel
        ? char.ToUpperInvariant(writtenName[0]) + writtenName[1..]
        : $"The image {writtenName}";

    /// <summary>
    /// What to do about a missing image. An embedded one was never uploaded, and the PDF reads images from storage only
    /// (LaTeXExportService.CollectImagesAsync), so "upload it again" would be wrong advice for it.
    /// </summary>
    public static string MissingAdvice(string writtenName) => writtenName == EmbeddedLabel
        ? "It is embedded in the block, and the PDF only reads uploaded images: upload it as a file."
        : "Upload it again.";

    private static readonly Regex UploadedKey = new(
        @"(?:^|/)documents/[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}/images/[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}(?:\.\w+)?$",
        RegexOptions.Compiled);

    public static string Name(string? src, int index = 0)
    {
        if (TexLiveImage(src) is { } texLive) return texLive;
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
    /// instead (RenderService prints those and not the figure's src); and a CV's photo, which printed as a
    /// "[photo]" frame because its file never reached figures/ (the launch bot's #114, 15 Nov 2026).
    /// </summary>
    public static IEnumerable<(Block Block, string Src)> Sources(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks.Where(b => b.Type is "figure" or "photo"))
        {
            var content = block.Content.RootElement;
            if (content.ValueKind != JsonValueKind.Object) continue;
            if (block.Type == "photo")
            {
                if (content.TryGetProperty("src", out var ps) && ps.ValueKind == JsonValueKind.String && ps.GetString() is { Length: > 0 } photo)
                    yield return (block, photo);
                continue;
            }
            if (content.TryGetProperty("subfigures", out var subs) && subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0)
            {
                foreach (var sub in subs.EnumerateArray())
                    if (sub.ValueKind == JsonValueKind.Object && sub.TryGetProperty("src", out var ss) && ss.GetString() is { Length: > 0 } subSrc
                        && TexLiveImage(subSrc) is null)
                        yield return (block, subSrc);
            }
            else if (content.TryGetProperty("src", out var s) && s.GetString() is { Length: > 0 } src && TexLiveImage(src) is null)
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

    /// <summary>The storage key a figure's src names (a key, /uploads/key, or a URL); null when none.</summary>
    public static string? StorageKey(string? src)
    {
        if (string.IsNullOrEmpty(src)) return null;
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(src, UriKind.Absolute, out var uri)) return null;
            var path = uri.AbsolutePath;
            // /uploads/key (local storage), or an R2/S3 path as it is.
            return path.StartsWith("/uploads/", StringComparison.Ordinal) ? path["/uploads/".Length..] : path.TrimStart('/');
        }
        return src.StartsWith("/uploads/", StringComparison.Ordinal) ? src["/uploads/".Length..] : src;
    }

    /// <summary>The image, or a framed placeholder naming the missing file.</summary>
    /// <param name="shownAs">The name the frame prints for a missing image: the file as the source wrote it
    /// (<see cref="WrittenName"/>). The frame printed the export's file name, "[Missing figure: image-0.png]" for
    /// \includegraphics{lilia-launch-no-such-image}, a name the author never wrote (launch-bot #244 note, 19 Nov 2026).
    /// The file included is still figures/<paramref name="filename"/>.</param>
    public static string Include(string filename, string width, string placeholderWidth = @"0.7\textwidth", string? shownAs = null) =>
        TexLiveImage(filename) is { } texLive
            ? $@"\includegraphics[width={width}]{{{texLive}}}"
            : $@"\IfFileExists{{figures/{filename}}}{{%" + "\n"
        + $@"  \includegraphics[width={width}]{{figures/{filename}}}%" + "\n"
        // No \centering in the frame: the figure's own alignment (left, right) places it.
        + $@"}}{{\fbox{{\parbox{{{placeholderWidth}}}{{\small\textit{{[Missing figure: {LatexText.Escape(shownAs ?? filename)}]}}}}}}}}";
}
