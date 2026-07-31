using System.Globalization;

namespace Lilia.Core.Blocks;

/// <summary>
/// Reads a block → page map out of a LaTeX <c>.aux</c> file.
///
/// <para><b>Why the .aux and not the PDF.</b> LaTeX already writes the page
/// number of every label, on every run, into a file we already produce. Reading
/// it is one parse of existing output — no PDF text extraction, no heuristics,
/// no second tool. The document render emits <c>\label{blk-&lt;id&gt;}</c> per
/// block (see <see cref="LabelPrefix"/>), and LaTeX does the rest.</para>
///
/// <para><b>The format, and why this is not a regex.</b> An entry looks like:</para>
/// <code>
/// \newlabel{blk-7f3a}{{2.1}{7}{Some title}{section.2.1}{}}
/// </code>
/// <para>Page is the <b>second</b> brace group of the outer value. The groups
/// after it hold arbitrary user text — a section title can contain braces, and
/// with hyperref there are five groups rather than two. A regex like
/// <c>\{([^}]*)\}\{([^}]*)\}</c> reads the first two groups of a *flat* string
/// and quietly returns the wrong field the moment a title contains
/// <c>\textbf{x}</c>. So this counts brace depth instead.</para>
///
/// <para>Requires <b>two</b> LaTeX passes to be accurate: pass one writes the
/// .aux, pass two is when the page numbers settle. The compile path already
/// runs twice.</para>
/// </summary>
public static class AuxPageMap
{
    /// <summary>
    /// Prefix for per-block labels. Deliberately short — it lands in every
    /// document's .aux — and deliberately not a word an author would write,
    /// so a hand-written \label cannot collide with one of ours.
    /// </summary>
    public const string LabelPrefix = "blk-";

    /// <summary>The label a block is emitted with.</summary>
    public static string LabelFor(Guid blockId) => $"{LabelPrefix}{blockId}";

    /// <summary>
    /// Parse every <c>blk-</c> label into a block id → page number map.
    ///
    /// Entries that are not ours, are malformed, or carry a non-numeric page
    /// (LaTeX writes roman numerals for front matter, and <c>\thepage</c> can be
    /// redefined to anything) are skipped rather than throwing: a page map is a
    /// convenience layered on top of a compile that already succeeded, and
    /// failing the render because one label looked odd would be a worse outcome
    /// than a map with a hole in it.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> Parse(string? auxContent)
    {
        var map = new Dictionary<Guid, int>();
        if (string.IsNullOrEmpty(auxContent)) return map;

        var span = auxContent.AsSpan();
        var marker = @"\newlabel{".AsSpan();

        var index = 0;
        while (true)
        {
            var found = span[index..].IndexOf(marker, StringComparison.Ordinal);
            if (found < 0) break;

            var cursor = index + found + marker.Length;

            // Label name — a flat token, no nesting to worry about.
            var nameEnd = span[cursor..].IndexOf('}');
            if (nameEnd < 0) break;

            var name = span.Slice(cursor, nameEnd);
            cursor += nameEnd + 1;

            if (!name.StartsWith(LabelPrefix, StringComparison.Ordinal)
                || !Guid.TryParse(name[LabelPrefix.Length..], out var blockId))
            {
                index = cursor;
                continue;
            }

            // Value — the outer group holding the sub-groups.
            if (!TryReadGroup(span, cursor, out var value, out var afterValue))
            {
                index = cursor;
                continue;
            }

            // First sub-group is the reference number, second is the page.
            if (TryReadGroup(value, 0, out _, out var afterRef)
                && TryReadGroup(value, afterRef, out var page, out _)
                && int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageNumber))
            {
                // Last write wins: LaTeX appends a fresh .aux each run, but a
                // label redefined within one run means the later position is the
                // one the PDF actually reflects.
                map[blockId] = pageNumber;
            }

            index = afterValue;
        }

        return map;
    }

    /// <summary>
    /// Read one <c>{…}</c> group starting at <paramref name="start"/>, counting
    /// depth so nested braces inside the group are kept rather than ending it.
    /// Leading whitespace is skipped; anything else before the brace means this
    /// is not a group and the caller should give up on the entry.
    /// </summary>
    private static bool TryReadGroup(
        ReadOnlySpan<char> span, int start, out ReadOnlySpan<char> content, out int next)
    {
        content = default;
        next = start;

        var i = start;
        while (i < span.Length && char.IsWhiteSpace(span[i])) i++;
        if (i >= span.Length || span[i] != '{') return false;

        var depth = 0;
        var contentStart = i + 1;

        for (; i < span.Length; i++)
        {
            // A brace escaped as \{ is literal text, not structure. Skipping the
            // next character also covers \\ correctly.
            if (span[i] == '\\') { i++; continue; }

            if (span[i] == '{') depth++;
            else if (span[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    content = span[contentStart..i];
                    next = i + 1;
                    return true;
                }
            }
        }

        return false;
    }
}
