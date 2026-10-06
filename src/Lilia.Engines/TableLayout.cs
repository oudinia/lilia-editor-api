using System.Text;
using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// The rest of what a table MEANS (Olivia's 2g, document themes, after <see cref="TableColumnFormat"/>):
/// group headers, notes, row groups and long tables. Like the column format, all of it is stored in
/// the table, travels into every paper and into Copy LaTeX, and gives the same LaTeX under every
/// theme — a paper changes only how lilia-theme.sty draws it.
/// </summary>
/// <remarks>
/// <para><b>Stored</b> in the table content:</para>
/// <list type="bullet">
/// <item><c>headerGroups: [{ label, start, span }]</c> — a row above the header with
/// <c>\multicolumn{span}{c}{label}</c> over grid columns <c>start … start+span-1</c> (0-based) and a
/// trimmed <c>\cmidrule(lr)</c> under each group. Groups that overlap, run off the table or sit on a
/// table without a header are not written.</item>
/// <item><c>note</c> on a cell object (<c>{ content, note }</c>, header or body) — the cell gets a
/// <c>\tnote</c> mark and the note prints under the table (<c>threeparttable</c>, or
/// <c>threeparttablex</c> for a long table).</item>
/// <item><c>rowGroups: [i, …]</c> — body rows (0-based) that start a group: a <c>\midrule</c> before
/// each, after which a Banded paper restarts its stripes (lilia-theme.sty).</item>
/// </list>
/// <para><b>Derived, never stored:</b> the note marks (a, b, c… in reading order — header left to
/// right, then the body row by row; one note text written twice shares its mark), and whether the
/// table is long: more than <see cref="LongTableRows"/> body rows, or <c>longTable: true</c>. A long
/// table is a <c>longtable</c> whose header repeats on every page under a "(continued)" caption —
/// in a single-column document only, since longtable fails in two columns
/// (<see cref="RenderService.SupportsLongtable"/>).</para>
/// <para>The client mirrors this in <c>table-grid/tableLayout.ts</c>; change both together.</para>
/// </remarks>
public sealed class TableLayout
{
    /// <summary>A table with more body rows than this breaks across pages as a longtable.</summary>
    public const int LongTableRows = 40;

    /// <summary>A group header over grid columns <see cref="Start"/> … <see cref="Start"/>+<see cref="Span"/>-1.</summary>
    public sealed record HeaderGroup(int Start, int Span, string Label);

    /// <summary>A note under the table, with its mark.</summary>
    public sealed record Note(string Mark, string Text);

    private readonly Dictionary<(int Row, int Col), string> _marks;
    private readonly HashSet<int> _groupStarts;

    private TableLayout(List<HeaderGroup> groups, HashSet<int> groupStarts, List<Note> notes, Dictionary<(int, int), string> marks, bool isLong)
    {
        Groups = groups;
        _groupStarts = groupStarts;
        Notes = notes;
        _marks = marks;
        IsLong = isLong;
    }

    public IReadOnlyList<HeaderGroup> Groups { get; }
    public IReadOnlyList<Note> Notes { get; }
    public bool HasNotes => Notes.Count > 0;
    /// <summary>Over <see cref="LongTableRows"/> body rows, or marked <c>longTable</c>.</summary>
    public bool IsLong { get; }

    /// <summary>Whether body row <paramref name="row"/> starts a group (a rule goes before it).</summary>
    public bool StartsGroup(int row) => _groupStarts.Contains(row);

    /// <summary>
    /// Read a table's layout. <paramref name="colCount"/> is the emitter's column count;
    /// <paramref name="hasHeaders"/> whether it writes a header row (group headers need one).
    /// </summary>
    public static TableLayout Read(JsonElement content, int colCount, bool hasHeaders)
    {
        var rows = content.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Array).ToList()
            : [];

        // Group headers: inside the table, at least one column, never overlapping.
        var groups = new List<HeaderGroup>();
        if (hasHeaders && content.TryGetProperty("headerGroups", out var hg) && hg.ValueKind == JsonValueKind.Array)
        {
            var read = hg.EnumerateArray()
                .Where(g => g.ValueKind == JsonValueKind.Object)
                .Select(g => new HeaderGroup(Int(g, "start", -1), Int(g, "span", 1), Str(g, "label")))
                .Where(g => g.Start >= 0 && g.Span >= 1 && g.Start + g.Span <= colCount)
                .OrderBy(g => g.Start);
            var end = 0;
            foreach (var g in read)
            {
                if (g.Start < end) continue;
                groups.Add(g);
                end = g.Start + g.Span;
            }
        }

        // Row groups: a body row past the first, inside the table.
        var starts = new HashSet<int>();
        if (content.TryGetProperty("rowGroups", out var rg) && rg.ValueKind == JsonValueKind.Array)
            foreach (var x in rg.EnumerateArray())
                if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i) && i > 0 && i < rows.Count)
                    starts.Add(i);

        // Notes, marked in reading order; one text written twice shares its mark.
        var notes = new List<Note>();
        var marks = new Dictionary<(int, int), string>();
        void Take(JsonElement cell, int row, int col)
        {
            if (cell.ValueKind != JsonValueKind.Object || !cell.TryGetProperty("note", out var n) || n.ValueKind != JsonValueKind.String) return;
            var text = (n.GetString() ?? "").Trim();
            if (text.Length == 0) return;
            var existing = notes.FirstOrDefault(x => x.Text == text);
            if (existing is null)
            {
                existing = new Note(MarkFor(notes.Count), text);
                notes.Add(existing);
            }
            marks[(row, col)] = existing.Mark;
        }
        if (hasHeaders && content.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Array)
        {
            var c = 0;
            foreach (var h in hs.EnumerateArray()) Take(h, -1, c++);
        }
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var c = 0;
            foreach (var cell in rows[ri].EnumerateArray())
            {
                Take(cell, ri, c);
                c++;
            }
        }

        var isLong = (content.TryGetProperty("longTable", out var lt) && lt.ValueKind == JsonValueKind.True)
            || (content.TryGetProperty("longtable", out var lt2) && lt2.ValueKind == JsonValueKind.True)
            || rows.Count > LongTableRows;
        return new TableLayout(groups, starts, notes, marks, isLong);
    }

    /// <summary>Whether a table block's content makes it a long table (see <see cref="IsLong"/>).</summary>
    public static bool IsLongTable(JsonElement content) => Read(content, int.MaxValue, false).IsLong;

    /// <summary>a … z, then aa, ab … — enough for any table.</summary>
    public static string MarkFor(int i) =>
        i < 26 ? ((char)('a' + i)).ToString() : MarkFor(i / 26 - 1) + (char)('a' + i % 26);

    /// <summary>The mark on a cell, or null. Row -1 is the header; col is the cell's index in its stored row.</summary>
    public string? MarkAt(int row, int col) => _marks.TryGetValue((row, col), out var m) ? m : null;

    /// <summary>Rendered text cell with its <c>\tnote</c>, when it has one.</summary>
    public string WithNote(int row, int col, string rendered) =>
        MarkAt(row, col) is { } m ? rendered + $@"\tnote{{{m}}}" : rendered;

    /// <summary>
    /// An S column's number with its note: the mark braced after the number, which siunitx sets as
    /// text after it and keeps out of the alignment.
    /// </summary>
    public string NumberWithNote(int row, int col, string number) =>
        MarkAt(row, col) is { } m ? number + $@"{{\tnote{{{m}}}}}" : number;

    /// <summary>
    /// The group header row (<c>\liliaHeadRow</c>, a <c>\multicolumn</c> per group, empty cells
    /// between) and the trimmed rules under it, or null without groups. Group labels are header
    /// cells: <paramref name="headerCell"/> formats them as the emitter formats its header.
    /// </summary>
    public string? GroupHeaderLines(int colCount, Func<string, string> headerCell)
    {
        if (Groups.Count == 0) return null;
        var cells = new List<string>();
        var col = 0;
        foreach (var g in Groups)
        {
            while (col < g.Start) { cells.Add(""); col++; }
            cells.Add($@"\multicolumn{{{g.Span}}}{{c}}{{{headerCell(g.Label)}}}");
            col += g.Span;
        }
        while (col < colCount) { cells.Add(""); col++; }
        var rules = string.Join(" ", Groups.Select(g => $@"\cmidrule(lr){{{g.Start + 1}-{g.Start + g.Span}}}"));
        return LatexText.HeadRow + string.Join(" & ", cells) + " \\\\\n" + rules;
    }

    /// <summary><c>\begin{tablenotes}</c> … for a tabular in a threeparttable.</summary>
    public string NotesList()
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{tablenotes}\footnotesize");
        foreach (var n in Notes) sb.AppendLine($@"\item[{n.Mark}] {LatexText.EscapeCell(n.Text)}");
        sb.Append(@"\end{tablenotes}");
        return sb.ToString();
    }

    /// <summary><c>\begin{TableNotes}</c> … for a longtable in a ThreePartTable (threeparttablex).</summary>
    public string LongNotesList()
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"\begin{TableNotes}\footnotesize");
        foreach (var n in Notes) sb.AppendLine($@"\item[{n.Mark}] {LatexText.EscapeCell(n.Text)}");
        sb.Append(@"\end{TableNotes}");
        return sb.ToString();
    }

    /// <summary>
    /// A longtable's head: the first one with the caption, then the one every later page repeats,
    /// under "(continued)". <paramref name="captionRow"/> is the caption and label row (ending in
    /// <c>\\</c>), or empty; <paramref name="headRows"/> the group and header rows, or empty for a
    /// table without a header, which then has nothing to repeat.
    /// </summary>
    public static string LongTableHead(string captionRow, bool hasCaption, string headRows)
    {
        var sb = new StringBuilder();
        if (captionRow.Length > 0) sb.AppendLine(captionRow);
        sb.AppendLine(@"\toprule");
        if (headRows.Length == 0) return sb.ToString().TrimEnd();
        sb.AppendLine(headRows);
        sb.AppendLine(@"\midrule");
        sb.AppendLine(@"\endfirsthead");
        // \caption[] keeps it out of the List of Tables; the number stays the table's.
        if (hasCaption) sb.AppendLine(@"\caption[]{(continued)}\\");
        sb.AppendLine(@"\toprule");
        sb.AppendLine(headRows);
        sb.AppendLine(@"\midrule");
        sb.Append(@"\endhead");
        return sb.ToString();
    }

    private static int Int(JsonElement o, string p, int fallback) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

    private static string Str(JsonElement o, string p) =>
        o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
