using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// A beamer frame whose content is taller than the frame, named by its number (Olivia, 6 Oct
/// 2026: "Keep the 14 pt floor, but don't let an overflow go unnoticed"). The themes never shrink
/// a frame to make it fit; validation says which frame does not fit instead, and the author
/// splits it or moves a block on.
///
/// <para><b>How a frame is named.</b> Beamer reports an overflowing frame only as
/// <c>Overfull \vbox (43.2pt too high) detected at line 16</c>: no frame number, and a line in a
/// generated file. The preview and validation compiles of a deck therefore carry
/// <see cref="Marker"/>, which writes <c>Lilia frame 7 ends on input line 16</c> to the log as
/// each page is shipped out. The overflow is reported while the frame is being made into a page,
/// so the marker that follows it names its frame; the marker's line is the frame's
/// <c>\end{frame}</c>, which <see cref="LatexLineMap"/> turns into the slide block.</para>
/// </summary>
public static class FrameOverflow
{
    /// <summary>
    /// One preamble line for a beamer deck's preview and validation compiles (not the .tex
    /// download): logs each shipped page's frame number and the line it ended on.
    /// </summary>
    public const string Marker =
        @"\AddToHook{shipout/before}{\typeout{Lilia frame \insertframenumber\space ends on input line \the\inputlineno}}";

    /// <summary>The validation issue for one frame, in Olivia's words.</summary>
    public static string Message(int frame) =>
        $"Frame {frame} doesn't fit at this theme's size. Split it, or move a block to the next frame.";

    /// <summary>An overflowing frame: its number, and the input line its page ended on.</summary>
    public sealed record Overflow(int Frame, int Line);

    private static readonly Regex OverfullVbox = new(
        @"Overfull \\vbox \(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FrameLine = new(
        @"Lilia frame (\d+) ends on input line (\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The frames that overflowed, once each, in deck order. Empty when the log has no frame
    /// markers (not a deck, or compiled without <see cref="Marker"/>) or nothing overflowed. A
    /// frame with overlays ships a page per slide and may overflow on each; it is named once.
    /// </summary>
    public static IReadOnlyList<Overflow> Find(string? log)
    {
        if (string.IsNullOrEmpty(log)) return [];

        var found = new List<Overflow>();
        var pending = false;
        foreach (var line in log.Replace("\r\n", "\n").Split('\n'))
        {
            if (OverfullVbox.IsMatch(line))
            {
                pending = true;
                continue;
            }

            var frame = FrameLine.Match(line);
            if (!frame.Success) continue;
            if (pending
                && int.TryParse(frame.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && int.TryParse(frame.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
                && !found.Any(f => f.Frame == number))
            {
                found.Add(new Overflow(number, at));
            }
            pending = false;
        }
        return found;
    }

    /// <summary>The validation messages for a compile log, one per overflowing frame.</summary>
    public static IReadOnlyList<string> Messages(string? log) => Find(log).Select(f => Message(f.Frame)).ToList();

    /// <summary>
    /// Each overflowing frame of an assembled deck with the block that wrote it (a slide block,
    /// or an embed block with its own frames), through the deck's <c>% block:&lt;id&gt;</c>
    /// markers. Null for a frame no block wrote (the title frame).
    /// </summary>
    public static IReadOnlyList<(int Frame, Guid? BlockId)> InDocument(string? latex, string? log)
    {
        var map = LatexLineMap.Parse(latex);
        return Find(log).Select(f => (f.Frame, map.BlockAt(f.Line))).ToList();
    }

    /// <summary>
    /// How many frames come before this block in the assembled deck, for per-block validation,
    /// which compiles the block on its own and primes the frame counter with this so the block's
    /// frame keeps its number. The title frame (the deck opens with <c>\maketitle</c>), one per
    /// slide block, each <c>\begin{frame}</c> in an embed block's code, and, when the author's
    /// preamble turns on section divider frames (<c>\AtBeginSection</c>), one per numbered
    /// section. The full-deck validation reads the real numbers from the compile.
    /// </summary>
    public static int FramesBefore(IEnumerable<Block> preceding, string? customPreamble)
    {
        var dividers = customPreamble is not null
            && customPreamble.Contains(@"\AtBeginSection", StringComparison.Ordinal)
            && customPreamble.Contains(@"\begin{frame}", StringComparison.Ordinal);

        var frames = 1;
        foreach (var block in preceding)
        {
            var type = block.Type.ToLowerInvariant();
            var content = block.Content.RootElement;
            if (type == "slide")
            {
                frames++;
            }
            else if (type == "embed" && content.ValueKind == JsonValueKind.Object
                     && content.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            {
                frames += Regex.Matches(code.GetString() ?? "", @"\\begin\{frame\}").Count;
            }
            else if (dividers && type is "heading" or "header" && content.ValueKind == JsonValueKind.Object
                     && (!content.TryGetProperty("level", out var level) || level.ValueKind != JsonValueKind.Number || level.GetInt32() == 1)
                     && !(content.TryGetProperty("numbered", out var numbered) && numbered.ValueKind == JsonValueKind.False))
            {
                frames++;
            }
        }
        return frames;
    }
}
