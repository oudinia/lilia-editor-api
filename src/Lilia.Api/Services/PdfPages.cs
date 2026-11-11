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

    public static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';

    [GeneratedRegex(@"/Type\s*/Pages\b[^<>]*?/Count\s+(?<n>\d+)|/Count\s+(?<n>\d+)[^<>]*?/Type\s*/Pages\b")]
    private static partial Regex CountEntry();

    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageObject();
}
