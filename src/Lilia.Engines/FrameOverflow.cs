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
/// <c>Overfull \vbox (43.2pt too high) detected at line 16</c>: no frame number, and a line in the
/// generated file. That line is inside the frame's <c>\begin{frame}…\end{frame}</c>, so the frame
/// is found in the source, and its number is counted there as beamer counts it: the title frame
/// (<c>\maketitle</c>), every <c>\begin{frame}</c>, and a section divider frame per numbered
/// <c>\section</c> when the preamble turns them on (<c>\AtBeginSection</c>).
/// <see cref="FramesBefore"/> counts the same way from the blocks, so a slide validated on its own
/// keeps the number the whole deck gives it.</para>
///
/// <para>Until 7 Oct 2026 the compile logged a marker as each page was shipped out, and an
/// overflow was given to the marker that followed it: a preamble hook, log lines TeX may wrap
/// mid-marker, and a page's shipping standing in for its place in the source. The source gives
/// the number without any of them.</para>
/// </summary>
public static class FrameOverflow
{
    /// <summary>The validation issue for one frame, in Olivia's words.</summary>
    public static string Message(int frame) =>
        $"Frame {frame} doesn't fit at this theme's size. Split it, or move a block to the next frame.";

    /// <summary>
    /// A frame of a deck's source: its number as beamer shows it, and the lines it spans (one
    /// line for the title frame and a section divider, the <c>\maketitle</c> or <c>\section</c>
    /// that makes it).
    /// </summary>
    public sealed record Frame(int Number, int Start, int End);

    /// <summary>An overflowing frame: its number, and the source line it starts on.</summary>
    public sealed record Overflow(int Frame, int Line);

    // "Overfull \vbox (282.1pt too high) detected at line 390"; TeX starts it on a line of its
    // own, and it is short enough never to be wrapped. "…while \output is active" has no line.
    private static readonly Regex OverfullVbox = new(
        @"^Overfull \\vbox \([^)\n]*\)[^\n]*?\bat lines? (\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex BeamerClass = new(
        @"^\s*\\documentclass\s*(?:\[[^\]]*\])?\s*\{\s*beamer\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    // What makes or numbers a frame, in the order TeX meets it. \begin{frame} takes its overlay
    // specification and options, to see noframenumbering; \section is not \section* or \sectionpage.
    private static readonly Regex FrameToken = new(
        @"\\begin\s*\{frame\}(?<opts>(?:\s*<[^>\n]*>)?(?:\s*\[[^\]\n]*\])*)"
        + @"|(?<end>\\end\s*\{frame\})"
        + @"|(?<title>\\maketitle)(?![A-Za-z@])"
        + @"|(?<section>\\section)(?![A-Za-z@*])"
        + @"|\\setcounter\s*\{framenumber\}\s*\{\s*(?<set>\d+)\s*\}"
        + @"|(?<stop>\\end\s*\{document\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BeginDocument = new(
        @"\\begin\s*\{document\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The frames of an assembled deck in source order. Empty when it is not a beamer document.
    /// A frame with overlays is one frame, however many pages it makes.
    /// </summary>
    public static IReadOnlyList<Frame> Frames(string? latex)
    {
        if (string.IsNullOrEmpty(latex)) return [];
        var lines = latex.Replace("\r\n", "\n").Split('\n').Select(StripComment).ToArray();

        var begin = Array.FindIndex(lines, l => BeginDocument.IsMatch(l));
        if (begin < 0) return [];
        var preamble = string.Join("\n", lines[..begin]);
        if (!BeamerClass.IsMatch(preamble)) return [];

        var found = new List<Frame>();
        // The body starts after \begin{document} on its line.
        var opener = BeginDocument.Match(lines[begin]);
        lines[begin] = lines[begin][(opener.Index + opener.Length)..];
        Scan(lines, begin, DividersOn(preamble), 0, found);
        return found;
    }

    /// <summary>
    /// The frames that overflowed, once each, in deck order: each <c>Overfull \vbox</c> in the
    /// log belongs to the frame whose source holds the line it was detected at (the last frame
    /// that starts at or before it). Empty when the document is not a deck or nothing overflowed.
    /// A frame with overlays may overflow on each of its pages; it is named once.
    /// </summary>
    public static IReadOnlyList<Overflow> Find(string? latex, string? log)
    {
        if (string.IsNullOrEmpty(log)) return [];
        var frames = Frames(latex);
        if (frames.Count == 0) return [];

        var found = new List<Overflow>();
        foreach (Match m in OverfullVbox.Matches(log.Replace("\r\n", "\n")))
        {
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line)) continue;
            var frame = frames.LastOrDefault(f => f.Start <= line);
            if (frame is null || found.Any(f => f.Frame == frame.Number)) continue;
            found.Add(new Overflow(frame.Number, frame.Start));
        }
        return found.OrderBy(f => f.Line).ToList();
    }

    /// <summary>The validation messages for a compile, one per overflowing frame.</summary>
    public static IReadOnlyList<string> Messages(string? latex, string? log) =>
        Find(latex, log).Select(f => Message(f.Frame)).ToList();

    /// <summary>
    /// Each overflowing frame of an assembled deck with the block that wrote it (a slide block,
    /// or an embed block with its own frames), through the deck's <c>% block:&lt;id&gt;</c>
    /// markers. Null for a frame no block wrote (the title frame).
    /// </summary>
    public static IReadOnlyList<(int Frame, Guid? BlockId)> InDocument(string? latex, string? log)
    {
        var map = LatexLineMap.Parse(latex);
        return Find(latex, log).Select(f => (f.Frame, map.BlockAt(f.Line))).ToList();
    }

    /// <summary>
    /// How many frames come before this block in the assembled deck, for per-block validation,
    /// which compiles the block on its own and primes the frame counter with this so the block's
    /// frame keeps its number. Counted as <see cref="Frames"/> counts the assembled deck: the
    /// title frame (the deck opens with <c>\maketitle</c>), one per slide block, the frames in an
    /// embed block's code, and, when the author's preamble turns on section divider frames
    /// (<c>\AtBeginSection</c>), one per numbered section.
    /// </summary>
    public static int FramesBefore(IEnumerable<Block> preceding, string? customPreamble)
    {
        var dividers = DividersOn(customPreamble);

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
                var lines = (code.GetString() ?? "").Replace("\r\n", "\n").Split('\n').Select(StripComment).ToArray();
                frames = Scan(lines, 0, dividers, frames, null);
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

    /// <summary>
    /// Whether a preamble turns on section divider frames: an <c>\AtBeginSection</c> that makes a
    /// frame.
    /// </summary>
    private static bool DividersOn(string? preamble)
    {
        if (string.IsNullOrEmpty(preamble)) return false;
        var code = string.Join("\n", preamble.Replace("\r\n", "\n").Split('\n').Select(StripComment));
        return code.Contains(@"\AtBeginSection", StringComparison.Ordinal)
            && code.Contains(@"\begin{frame}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks source lines (comments already stripped) from <paramref name="from"/>, numbering
    /// frames from <paramref name="number"/> as beamer's frame counter does, and adds each to
    /// <paramref name="found"/> with its lines (1-based). Returns the counter at the end.
    /// </summary>
    private static int Scan(string[] lines, int from, bool dividers, int number, List<Frame>? found)
    {
        (int Number, int Start)? open = null;
        for (var i = from; i < lines.Length; i++)
        {
            var line = i + 1;
            foreach (Match m in FrameToken.Matches(lines[i]))
            {
                if (m.Groups["stop"].Success)
                {
                    if (open is { } o) found?.Add(new Frame(o.Number, o.Start, line));
                    return number;
                }
                if (m.Groups["set"].Success)
                {
                    if (int.TryParse(m.Groups["set"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var set))
                        number = set;
                }
                else if (m.Groups["end"].Success)
                {
                    if (open is { } o) found?.Add(new Frame(o.Number, o.Start, line));
                    open = null;
                }
                else if (m.Groups["opts"].Success)
                {
                    if (open is not null) continue; // frames don't nest
                    if (!m.Groups["opts"].Value.Contains("noframenumbering", StringComparison.Ordinal)) number++;
                    open = (number, line);
                }
                else if (open is null && (m.Groups["title"].Success || (dividers && m.Groups["section"].Success)))
                {
                    number++;
                    found?.Add(new Frame(number, line, line));
                }
            }
        }
        if (open is { } last) found?.Add(new Frame(last.Number, last.Start, lines.Length));
        return number;
    }

    /// <summary>A source line without its comment (from the first <c>%</c> not escaped).</summary>
    private static string StripComment(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '%') continue;
            var slashes = 0;
            for (var j = i - 1; j >= 0 && line[j] == '\\'; j--) slashes++;
            if (slashes % 2 == 0) return line[..i];
        }
        return line;
    }
}
