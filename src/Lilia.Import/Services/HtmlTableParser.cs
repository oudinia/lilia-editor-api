using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Parses HTML table markup (from MinerU's table_body) into ImportTableCell rows.
/// </summary>
public static partial class HtmlTableParser
{
    /// <summary>
    /// Parse an HTML table string into rows of ImportTableCell.
    /// Returns (rows, hasHeaderRow).
    /// </summary>
    public static (List<List<ImportTableCell>> Rows, bool HasHeaderRow) Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return ([], false);

        var rows = new List<List<ImportTableCell>>();
        var hasHeaderRow = false;

        // Detect if there's a <thead> section
        var hasTheadMatch = TheadRegex().Match(html);
        var theadEndIndex = hasTheadMatch.Success ? hasTheadMatch.Index + hasTheadMatch.Length : -1;

        // Find all <tr> elements
        var trMatches = TrRegex().Matches(html);

        foreach (Match trMatch in trMatches)
        {
            var trContent = trMatch.Groups[1].Value;
            var cells = new List<ImportTableCell>();

            // Determine if this row is in the <thead> section
            var isInThead = theadEndIndex > 0 && trMatch.Index < theadEndIndex;

            // Find all <td> and <th> elements
            var cellMatches = CellRegex().Matches(trContent);

            foreach (Match cellMatch in cellMatches)
            {
                var tag = cellMatch.Groups[1].Value.ToLowerInvariant();
                var attributes = cellMatch.Groups[2].Value;
                var cellContent = cellMatch.Groups[3].Value;

                // Strip inner HTML tags to get plain text
                var text = StripHtmlTags(cellContent).Trim();

                var colSpan = 1;
                var rowSpan = 1;

                var colSpanMatch = ColspanRegex().Match(attributes);
                if (colSpanMatch.Success && int.TryParse(colSpanMatch.Groups[1].Value, out var cs))
                    colSpan = cs;

                var rowSpanMatch = RowspanRegex().Match(attributes);
                if (rowSpanMatch.Success && int.TryParse(rowSpanMatch.Groups[1].Value, out var rs))
                    rowSpan = rs;

                cells.Add(new ImportTableCell
                {
                    Text = text,
                    ColSpan = colSpan,
                    RowSpan = rowSpan
                });

                // If we find <th> elements, this is a header row
                if (tag == "th" || isInThead)
                    hasHeaderRow = true;
            }

            if (cells.Count > 0)
                rows.Add(cells);
        }

        // If hasHeaderRow is true, only the first row is the header
        // (we set the flag but don't rearrange rows — caller uses HasHeaderRow)
        return (rows, hasHeaderRow);
    }

    private static string StripHtmlTags(string html)
    {
        return HtmlTagRegex().Replace(html, "")
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&")
            .Replace("&quot;", "\"");
    }

    [GeneratedRegex(@"<thead[\s>].*?</thead>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TheadRegex();

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TrRegex();

    [GeneratedRegex(@"<(td|th)([^>]*)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ColspanRegex();

    [GeneratedRegex(@"rowspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RowspanRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
