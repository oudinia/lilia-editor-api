using System.Globalization;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Tables wider than their column (MCP-63, run 3: three papers overflowed into the margin or the
/// other column, under a clean check). TeX says so as "Overfull \hbox (…pt too wide) in alignment at
/// lines a--b"; the validation dropped every \hbox warning as cosmetic, which is true of a line of
/// prose a point or two too wide, not of a table.
/// </summary>
public static class WideTables
{
    public sealed record Overflow(int FirstLine, int LastLine, double Points);

    private static readonly Regex Overfull = new(
        @"Overfull \\hbox \((?<pt>[0-9.]+)pt too wide\) in (?:alignment|paragraph) at lines (?<a>\d+)--(?<b>\d+)", RegexOptions.Compiled);
    private static readonly Regex TabularBegin = new(@"\\begin\{(?:tabular\*?|tabularx|longtable|tabulary|array)\}", RegexOptions.Compiled);
    private static readonly Regex TabularEnd = new(@"\\end\{(?:tabular\*?|tabularx|longtable|tabulary|array)\}", RegexOptions.Compiled);

    /// <summary>
    /// One entry per table (its widest overfull box), at least <paramref name="minPoints"/> too wide.
    /// TeX reports a tabular as "in paragraph" (its box sits in the float's paragraph) and a longtable
    /// row as "in alignment"; either counts when its lines overlap a tabular in <paramref name="source"/>.
    /// </summary>
    public static IReadOnlyList<Overflow> Find(string? log, string? source, double minPoints = 10)
    {
        if (string.IsNullOrEmpty(log) || string.IsNullOrEmpty(source)) return [];
        var spans = new List<(int From, int To)>();
        var lines = source.Split('\n');
        int? open = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (open is null && TabularBegin.IsMatch(lines[i])) open = i + 1;
            if (open is not null && TabularEnd.IsMatch(lines[i])) { spans.Add((open.Value, i + 1)); open = null; }
        }
        return Overfull.Matches(log)
            .Select(m => new Overflow(int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["pt"].Value, CultureInfo.InvariantCulture)))
            .Where(o => o.Points >= minPoints)
            .Select(o => (o, span: spans.FirstOrDefault(sp => sp.From <= o.LastLine && o.FirstLine <= sp.To)))
            .Where(x => x.span != default)
            .GroupBy(x => x.span)
            .Select(g => g.MaxBy(x => x.o.Points).o)
            .ToList();
    }

    public static string Message(Overflow o) =>
        $"A table is {Math.Round(o.Points):0} pt wider than the text (source lines {o.FirstLine}–{o.LastLine}): it runs into the margin. "
        + "Shorten its cells, let a column wrap, or in two columns set it to span both.";
}
