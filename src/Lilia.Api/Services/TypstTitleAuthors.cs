using System.Text;
using Lilia.Engines;

namespace Lilia.Api.Services;

/// <summary>
/// The multi-author form of the Typst title block: a centred grid with one
/// column per author (three to a row), the note mark as a superscript after the
/// name, and the <c>\thanks</c> notes as small lines under the block.
/// A single author without notes is not handled here: the callers keep the
/// flat one-line form they always had.
/// </summary>
internal static class TypstTitleAuthors
{
    internal const int MaxColumns = 3;

    /// <summary>Whether the grid form applies: several authors, or any note.</summary>
    internal static bool UseGrid(IReadOnlyList<TitleAuthor> authors) =>
        authors.Count > 1 || authors.Any(a => a.Notes.Count > 0);

    internal static IReadOnlyList<TitleAuthor> ParseForGrid(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return [];
        var authors = TitleAuthors.Parse(author);
        return UseGrid(authors) ? authors : [];
    }

    /// <summary>The body of <c>author: (...)</c>: a plain array of the names, each quoted by <paramref name="quote"/>.</summary>
    internal static string NameArray(IReadOnlyList<TitleAuthor> authors, Func<string, string> quote)
    {
        var names = authors.Select(a => a.Name).Where(n => n.Length > 0).Select(quote).ToList();
        return names.Count == 1 ? names[0] + "," : string.Join(", ", names);
    }

    internal static void AppendGrid(StringBuilder sb, IReadOnlyList<TitleAuthor> authors)
    {
        var cols = Math.Min(authors.Count, MaxColumns);
        sb.AppendLine("  #grid(");
        sb.AppendLine($"    columns: ({string.Join(", ", Enumerable.Repeat("1fr", cols))}{(cols == 1 ? "," : "")}),");
        sb.AppendLine("    column-gutter: 1.5em,");
        sb.AppendLine("    row-gutter: 0.8em,");
        sb.AppendLine("    align: center,");
        foreach (var a in authors)
        {
            var cell = new StringBuilder(TypstExportService.EscapeInline(a.Name));
            if (a.NoteSymbols.Count > 0)
                cell.Append("#super[").Append(TypstExportService.EscapeInline(string.Join(",", a.NoteSymbols))).Append(']');
            foreach (var line in a.Lines)
                cell.Append(" \\ ").Append(TypstExportService.EscapeInline(line));
            sb.AppendLine($"    [{cell}],");
        }
        sb.AppendLine("  )");
    }

    /// <summary>The \thanks footnotes, small, under the centred block.</summary>
    internal static void AppendNotes(StringBuilder sb, IReadOnlyList<TitleAuthor> authors)
    {
        var lines = new List<string>();
        foreach (var a in authors)
            for (var i = 0; i < a.Notes.Count; i++)
                lines.Add($"#super[{TypstExportService.EscapeInline(a.NoteSymbols[i])}] {TypstExportService.EscapeInline(a.Notes[i])}");
        if (lines.Count == 0) return;
        sb.AppendLine("#text(size: 0.85em)[");
        sb.AppendLine("  " + string.Join(" \\\n  ", lines));
        sb.AppendLine("]");
    }
}
