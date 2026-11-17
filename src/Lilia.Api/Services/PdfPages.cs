using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// How many pages a PDF has, read from the file before it goes to Mathpix (PDF import is paid per page, 11 Nov 2026).
/// Without a PDF library: the largest page-tree <c>/Count</c> (read within one dictionary), or else the number of
/// <c>/Type /Page</c> objects. A rough pre-check only: pdflatex compresses its page tree (it reads as 1), and the
/// launch bot's 2-page test PDF read as 21 (#65, 11 Nov 2026). What a plan is charged is the pages Mathpix reports. A PDF
/// whose page tree is compressed shows neither; it counts as one page here, and the import records the pages Mathpix
/// read. An estimate for the allowance, never a reason to refuse a real PDF.
/// </summary>
public static partial class PdfPages
{
    public static int Estimate(byte[] pdf)
    {
        // Latin-1 keeps every byte as one character, so the PDF's ASCII keywords read as they are.
        var text = Encoding.Latin1.GetString(pdf);
        var count = CountEntry().Matches(text).Select(m => int.TryParse(m.Groups["n"].Value, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        if (count > 0) return count;
        var pages = PageObject().Matches(text).Count;
        return Math.Max(1, pages);
    }

    /// <summary>
    /// The exact number of pages of a PDF Lilia made, or null when it can't be read: the page tree's root
    /// <c>/Count</c>, looked for in the file and inside its compressed object streams, where pdflatex puts it
    /// (check_document counted one LaTeX pass and said 3 pages for a 2-page PDF: launch-bot #125, 17 Nov 2026).
    /// </summary>
    public static int? Count(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var best = MaxCount(text);
        foreach (Match m in ObjectStream().Matches(text))
        {
            if (!m.Groups["dict"].Value.Contains("/FlateDecode", StringComparison.Ordinal)) continue;
            var start = m.Index + m.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end <= start) continue;
            try
            {
                using var input = new MemoryStream(pdf, start, end - start);
                using var z = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                z.CopyTo(output);
                best = Math.Max(best, MaxCount(Encoding.Latin1.GetString(output.ToArray())));
            }
            catch (InvalidDataException) { /* a stream cut short at its end: what was read is all there is */ }
        }
        return best > 0 ? best : null;
    }

    private static int MaxCount(string text) =>
        CountEntry().Matches(text).Select(m => int.TryParse(m.Groups["n"].Value, out var n) ? n : 0).DefaultIfEmpty(0).Max();

    [GeneratedRegex(@"<<(?<dict>(?:[^<>]|<<[^<>]*>>)*?/Type\s*/ObjStm(?:[^<>]|<<[^<>]*>>)*?)>>\s*stream\r?\n")]
    private static partial Regex ObjectStream();

    public static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';

    [GeneratedRegex(@"/Type\s*/Pages\b[^<>]*?/Count\s+(?<n>\d+)|/Count\s+(?<n>\d+)[^<>]*?/Type\s*/Pages\b")]
    private static partial Regex CountEntry();

    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageObject();
}
