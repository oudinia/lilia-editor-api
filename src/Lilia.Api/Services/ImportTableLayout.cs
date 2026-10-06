using Lilia.Import.Models;

namespace Lilia.Api.Services;

/// <summary>
/// An imported table's 2g layout written into its block content the way the editor and the
/// emitters store it (Lilia.Engines.TableLayout): a cell with a note is <c>{ content, note }</c>,
/// and <c>headerGroups</c> / <c>rowGroups</c> are written only when the table has them, so a
/// table without them imports exactly as before. Shared by the import job and latex-to-blocks.
/// </summary>
internal static class ImportTableLayout
{
    /// <summary>A cell as stored: its text, or <c>{ content, note }</c> when it carries a note.</summary>
    public static object Cell(ImportTable t, ImportTableCell c) =>
        c.NoteMark is { } mark && t.Notes.TryGetValue(mark, out var note)
            ? new { content = c.Text, note }
            : c.Text;

    public static object[] Row(ImportTable t, IEnumerable<ImportTableCell> row) =>
        row.Select(c => Cell(t, c)).ToArray();

    /// <summary>Adds <c>headerGroups</c> and <c>rowGroups</c> when the table has them.</summary>
    public static Dictionary<string, object> WithLayout(ImportTable t, Dictionary<string, object> content)
    {
        if (t.HeaderGroups.Count > 0)
            content["headerGroups"] = t.HeaderGroups.Select(g => new { label = g.Label, start = g.Start, span = g.Span }).ToArray();
        if (t.RowGroupStarts.Count > 0)
            content["rowGroups"] = t.RowGroupStarts.ToArray();
        return content;
    }
}
