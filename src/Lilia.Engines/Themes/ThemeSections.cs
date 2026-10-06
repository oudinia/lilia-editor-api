using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using HeadingCommands = Lilia.Core.Models.HeadingCommands;

namespace Lilia.Engines.Themes;

/// <summary>
/// Where a block sits among the document's top numbered headings, for the editor's lists (the
/// tables list's "Used in" chip, design 2b).
/// </summary>
/// <param name="Number">What the PDF prints for the top numbered heading above the block
/// (<c>"3"</c>; an appendix's letter, <c>"A"</c>), or null before the first one and under an
/// unnumbered one.</param>
/// <param name="Colour">The Index colour that heading takes, <c>#RRGGBB</c>, or null when the
/// document's look is not Index (or its class locks the look), and wherever Number is null.</param>
public sealed record SectionPlace(string? Number, string? Colour)
{
    public static readonly SectionPlace None = new(null, null);
}

/// <summary>
/// The top numbered heading each block sits under, with its number and, for an Index paper, its
/// colour, worked out the way the PDF does it. Derived on read, never stored.
///
/// <para>The top level is the one the theme line names: level 1 (<c>\chapter</c> in a class
/// that has chapters, <c>\section</c> elsewhere), or level 2 in a chapter class whose body has
/// no numbered level-1 heading (<c>top=section</c>). Embed blocks count too: a raw
/// <c>\chapter</c>/<c>\section</c> at the top level advances the number, a starred one is
/// unnumbered, and <c>\appendix</c> restarts the numbering in letters.</para>
///
/// <para>The colour mirrors <c>lilia-theme.sty</c>'s <c>\lilia@colourof</c>: pins are written
/// by number (<see cref="LaTeXPreambleBuilder.BuildThemeLine"/>), a pinned number takes its
/// colour, the others fill the colours no pin took, in order (all eight when every colour is
/// pinned); appendices restart the plain sequence and take no pins.</para>
/// </summary>
public static class ThemeSections
{
    private static readonly Regex EmbedCommand = new(
        @"\\(?<cmd>appendix|chapter|section)(?![A-Za-z@])(?<star>\*?)", RegexOptions.Compiled);

    /// <summary>
    /// Each block's place, keyed by block id. <paramref name="bodyBlocks"/> are the blocks the
    /// body prints, in order (the exporter's body: no title, abstract or bibliography block).
    /// </summary>
    public static IReadOnlyDictionary<Guid, SectionPlace> Places(
        IReadOnlyList<Block> bodyBlocks, string? documentClass, string? storedLook)
    {
        var look = DocumentLook.Parse(storedLook);
        var coloured = look.Theme == "index" && ThemeLock.Reason(documentClass) is null;
        var chapterClass = HeadingCommands.HasChapters(documentClass);
        var rawChapters = bodyBlocks.Any(LaTeXPreambleBuilder.PrintsChapter);
        // top=section in a chapter class with no numbered level-1 heading (and no raw \chapter).
        var topLevel = chapterClass && !rawChapters && !bodyBlocks.Any(LaTeXPreambleBuilder.IsNumberedTopHeading) ? 2 : 1;
        var topCommand = chapterClass && topLevel == 1 ? "chapter" : "section";

        // The pins as the theme line writes them: by the heading's number among the numbered
        // level-1 heading blocks; none when a raw \chapter puts that count out of step.
        var pins = new Dictionary<int, int>();
        if (coloured && !rawChapters && look.Pins.Count > 0)
        {
            var number = 0;
            foreach (var block in bodyBlocks.Where(LaTeXPreambleBuilder.IsNumberedTopHeading))
            {
                number++;
                if (look.Pins.TryGetValue(block.Id.ToString(), out var k)) pins[number] = k;
            }
        }
        var colourOf = coloured ? IndexColours(pins) : null;

        var places = new Dictionary<Guid, SectionPlace>();
        var current = SectionPlace.None;
        int n = 0, a = 0;
        var appendix = false;

        void Numbered()
        {
            if (appendix)
            {
                a++;
                current = new SectionPlace(Letters(a), colourOf is null ? null : ThemeCatalog.Sequence[(a - 1) % ThemeCatalog.SequenceLength]);
            }
            else
            {
                n++;
                var printed = topLevel == 2 ? $"0.{n}" : n.ToString();
                current = new SectionPlace(printed, colourOf?.Invoke(n));
            }
        }

        foreach (var block in bodyBlocks)
        {
            if (IsHeading(block))
            {
                var (level, numbered) = HeadingOf(block);
                // A numbered top heading starts a new place; an unnumbered one (or, under
                // top=section, a level-1 heading above it) leaves the block in neutral ink.
                if (level == topLevel && numbered) Numbered();
                else if (level <= topLevel) current = SectionPlace.None;
            }
            else if (IsAppendixBlock(block))
            {
                appendix = true; a = 0;
            }
            else if (string.Equals(block.Type, "embed", StringComparison.OrdinalIgnoreCase) && EmbedCode(block) is { } code)
            {
                foreach (Match m in EmbedCommand.Matches(code))
                {
                    var cmd = m.Groups["cmd"].Value;
                    if (cmd == "appendix") { appendix = true; a = 0; continue; }
                    if (cmd != topCommand) continue;
                    if (m.Groups["star"].Value == "*") current = SectionPlace.None;
                    else Numbered();
                }
            }
            places[block.Id] = current;
        }
        return places;
    }

    /// <summary>The place of one block, or <see cref="SectionPlace.None"/> when it is not in the body.</summary>
    public static SectionPlace PlaceOf(Guid blockId, IReadOnlyList<Block> bodyBlocks, string? documentClass, string? storedLook) =>
        Places(bodyBlocks, documentClass, storedLook).TryGetValue(blockId, out var place) ? place : SectionPlace.None;

    /// <summary>The n-th numbered top heading's colour outside the appendix (\lilia@colourof).</summary>
    private static Func<int, string> IndexColours(IReadOnlyDictionary<int, int> pins)
    {
        var taken = pins.Values.ToHashSet();
        var available = Enumerable.Range(0, ThemeCatalog.SequenceLength).Where(k => !taken.Contains(k)).ToList();
        if (available.Count == 0) available = Enumerable.Range(0, ThemeCatalog.SequenceLength).ToList();
        return n =>
        {
            if (pins.TryGetValue(n, out var pinned)) return ThemeCatalog.Sequence[pinned];
            var unpinned = Enumerable.Range(1, n).Count(i => !pins.ContainsKey(i));
            var u = Math.Max(0, unpinned - 1);
            return ThemeCatalog.Sequence[available[u % available.Count]];
        };
    }

    /// <summary>\Alph: A … Z (LaTeX stops at 26; beyond it this keeps going as AA, AB …).</summary>
    private static string Letters(int n)
    {
        var s = "";
        for (; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    /// <summary>The block starts the appendices: the Appendix block, or an embed that prints \appendix.</summary>
    public static bool StartsAppendix(Block block) =>
        IsAppendixBlock(block)
        || (string.Equals(block.Type, "embed", StringComparison.OrdinalIgnoreCase)
            && EmbedCode(block) is { } code
            && EmbedCommand.Matches(code).Any(m => m.Groups["cmd"].Value == "appendix"));

    /// <summary>The Appendix back-matter block, which prints <c>\appendix</c>.</summary>
    public static bool IsAppendixBlock(Block block)
    {
        if (!string.Equals(block.Type, "backMatter", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return block.Content.RootElement.TryGetProperty("subType", out var t)
                && t.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(t.GetString(), "appendix", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsHeading(Block block) =>
        string.Equals(block.Type, "heading", StringComparison.OrdinalIgnoreCase)
        || string.Equals(block.Type, "header", StringComparison.OrdinalIgnoreCase);

    private static (int Level, bool Numbered) HeadingOf(Block block)
    {
        try
        {
            var c = block.Content.RootElement;
            var level = c.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var lv) ? lv : 1;
            var numbered = !c.TryGetProperty("numbered", out var nb) || nb.ValueKind != JsonValueKind.False;
            return (Math.Max(1, level), numbered);
        }
        catch
        {
            return (1, true);
        }
    }

    private static string? EmbedCode(Block block)
    {
        try
        {
            var c = block.Content.RootElement;
            var code = c.TryGetProperty("code", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return string.IsNullOrEmpty(code) ? null : Regex.Replace(code, @"(?<!\\)%[^\r\n]*", "");
        }
        catch
        {
            return null;
        }
    }
}
