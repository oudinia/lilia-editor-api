using System.Globalization;
using System.Text.RegularExpressions;

namespace Lilia.Core.Blocks;

/// <summary>
/// Maps a line number in the assembled <c>.tex</c> back to the block that
/// produced it.
///
/// <para><b>Why this is needed.</b> P0.2 made page overflow visible, but the
/// warning LaTeX gives is <c>"Float too large for page by 1161.16pt on input
/// line 391"</c> — a line in a generated file the author has never seen. To act
/// on it, either to tell them which table is too tall or to re-emit that one
/// table as a <c>longtable</c>, the line has to become a block.</para>
///
/// <para><b>Read back out of the document rather than tracked during assembly.</b>
/// The renderer already writes a <c>% block:&lt;id&gt;</c> comment above every
/// block, so the information is present in the output and needs no extra
/// bookkeeping threaded through the assembly code — which would be one more
/// thing to keep in step, and would silently drift the first time someone
/// reorders an <c>AppendLine</c>.</para>
///
/// <para>Line numbers are 1-based, matching how TeX counts and reports them.</para>
/// </summary>
public sealed class LatexLineMap
{
    private readonly (int Line, Guid BlockId)[] _starts;

    private LatexLineMap((int Line, Guid BlockId)[] starts) => _starts = starts;

    /// <summary>Number of blocks located in the document.</summary>
    public int Count => _starts.Length;

    private static readonly Regex BlockMarker = new(
        @"^\s*%\s*block:([0-9a-fA-F-]{36})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Build the map by locating every <c>% block:&lt;id&gt;</c> marker.
    /// </summary>
    public static LatexLineMap Parse(string? latex)
    {
        if (string.IsNullOrEmpty(latex)) return new LatexLineMap([]);

        var starts = new List<(int, Guid)>();
        var lines = latex.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var match = BlockMarker.Match(lines[i]);
            if (match.Success && Guid.TryParse(match.Groups[1].Value, out var blockId))
                starts.Add((i + 1, blockId));
        }

        return new LatexLineMap([.. starts]);
    }

    /// <summary>
    /// The block covering <paramref name="line"/> — the last one whose marker is
    /// at or above it. Null when the line sits in the preamble, before any block.
    ///
    /// <para>"Covering" rather than "exactly at": TeX reports the line where it
    /// noticed a problem, which for an overfull box is somewhere inside the
    /// block's emitted body, not on its marker.</para>
    /// </summary>
    public Guid? BlockAt(int line)
    {
        Guid? found = null;
        foreach (var (start, blockId) in _starts)
        {
            if (start > line) break;
            found = blockId;
        }
        return found;
    }

    // "on input line 391" — how LaTeX ends the warnings that carry a position.
    private static readonly Regex InputLine = new(
        @"on input line (\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The blocks named by a set of warnings, in first-seen order.
    ///
    /// <para>Warnings with no line number are skipped rather than guessed at —
    /// several LaTeX warnings genuinely carry no position, and attributing one
    /// to whichever block happened to be last would point the author at innocent
    /// content. Silence about a block is recoverable; a confident wrong answer
    /// is what this whole plan exists to remove.</para>
    /// </summary>
    public IReadOnlyList<Guid> BlocksNamedBy(IEnumerable<string> warnings)
    {
        var found = new List<Guid>();

        foreach (var warning in warnings)
        {
            if (string.IsNullOrEmpty(warning)) continue;

            var match = InputLine.Match(warning);
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var line)) continue;

            var blockId = BlockAt(line);
            if (blockId is not null && !found.Contains(blockId.Value))
                found.Add(blockId.Value);
        }

        return found;
    }
}
