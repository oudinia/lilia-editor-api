using System.Text;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Launch-bot #76 (12 Nov 2026): a Word paragraph's bold, italic and underline reached the review as
/// spans that were then dropped, so "base tariff" lost its bold. Block text carries inline styling as
/// the editor's marks (the same ones the LaTeX importer writes): **bold**, *italic*, __underline__.
/// A span is narrowed to its non-blank characters, so a mark never opens or closes on a space.
/// </summary>
public static class InlineMarkers
{
    private static string? Mark(FormattingType type) => type switch
    {
        FormattingType.Bold => "**",
        FormattingType.Italic => "*",
        FormattingType.Underline => "__",
        _ => null,
    };

    public static string Apply(string text, IReadOnlyList<FormattingSpan>? spans)
    {
        if (string.IsNullOrEmpty(text) || spans is null || spans.Count == 0) return text;

        var marks = new List<(int Start, int End, string Mark)>();
        foreach (var s in spans)
        {
            if (Mark(s.Type) is not { } mark) continue;
            var start = Math.Clamp(s.Start, 0, text.Length);
            var end = Math.Clamp(s.Start + s.Length, start, text.Length);
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (end > start) marks.Add((start, end, mark));
        }
        if (marks.Count == 0) return text;

        // Merge touching spans of the same mark (Word splits one bold phrase into several runs).
        marks = marks.OrderBy(m => m.Mark).ThenBy(m => m.Start).ToList();
        var merged = new List<(int Start, int End, string Mark)>();
        foreach (var m in marks)
        {
            if (merged.Count > 0 && merged[^1].Mark == m.Mark
                && (m.Start <= merged[^1].End || text[merged[^1].End..m.Start].All(char.IsWhiteSpace)))
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.End, m.End), last.Mark);
            }
            else merged.Add(m);
        }

        var opens = merged.ToLookup(m => m.Start);
        var closes = merged.ToLookup(m => m.End);
        var sb = new StringBuilder(text.Length + merged.Count * 4);
        for (var i = 0; i <= text.Length; i++)
        {
            // Close the innermost (shortest) first, then open the outermost (longest) first.
            foreach (var m in closes[i].OrderBy(m => m.End - m.Start)) sb.Append(m.Mark);
            foreach (var m in opens[i].OrderByDescending(m => m.End - m.Start)) sb.Append(m.Mark);
            if (i < text.Length) sb.Append(text[i]);
        }
        return sb.ToString();
    }
}
