namespace Lilia.Engines;

/// <summary>
/// Whether a compiled table runs off the page, and by how much.
/// </summary>
/// <remarks>
/// The same two warnings the document-level auto-fit reads
/// (<c>POST /api/latex/{id}/pdf/auto-fit</c>, July): an <c>Overfull \vbox</c> is
/// un-floated content taller than the page, and <c>Float too large for page</c>
/// is a float that will not fit anywhere. Deliberately <b>not</b>
/// <c>Overfull \hbox</c> — that is a line sticking out by a few points, which is
/// a typesetting nag rather than a table that cannot be printed, and treating it
/// as one would convert every wide table into a longtable nobody asked for.
/// </remarks>
public static class TableOverflow
{
    /// <summary>How far past the page, in points, or null when it fits.</summary>
    public static double? TooTallBy(IEnumerable<string>? warnings)
    {
        if (warnings is null) return null;

        var offenders = warnings
            .Where(w => w.Contains(@"Overfull \vbox", StringComparison.Ordinal)
                     || w.Contains("Float too large for page", StringComparison.Ordinal))
            .ToArray();
        if (offenders.Length == 0) return null;

        // "(525.0pt too high)" for a vbox, "by 1161.16pt" for a float.
        var worst = offenders
            .Select(w => System.Text.RegularExpressions.Regex.Match(w, @"(?:\(|by )([\d.]+)pt"))
            .Where(m => m.Success)
            .Select(m => double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pt) ? pt : 0d)
            .DefaultIfEmpty(0d)
            .Max();

        // Overflowing by an unstated amount is still overflowing; 0 would read
        // as "fits" to any caller comparing against null.
        return worst > 0 ? worst : 0.01;
    }

    public static bool Overflows(IEnumerable<string>? warnings) => TooTallBy(warnings) is not null;
}
