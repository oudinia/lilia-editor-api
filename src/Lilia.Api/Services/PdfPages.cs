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

    /// <summary>
    /// The exact number of pages of an uploaded PDF, or null when it can't be read, for the plan's pre-check before
    /// Mathpix (the review of API #670, 20 Nov 2026: <see cref="Estimate"/> read a 2-page PDF as 21, and a Free account
    /// would have been refused a short PDF). In this order: poppler's <c>pdfinfo</c>, which reads the PDF as a viewer does
    /// (the latest revision, compressed page trees); else <see cref="Count"/>; else null. A null count is never a reason to
    /// refuse: the import then goes to Mathpix and is charged the pages Mathpix reports.
    /// </summary>
    /// <param name="pdfinfo">The pdfinfo step (tests replace it); <see cref="PdfInfoPages"/> by default.</param>
    public static int? Exact(byte[] pdf, Func<byte[], int?>? pdfinfo = null) =>
        (pdfinfo ?? (b => PdfInfoPages(b)))(pdf) ?? Count(pdf);

    /// <summary>
    /// <c>pdfinfo</c>'s <c>Pages:</c> for the bytes, or null (pdfinfo missing, failed, or slower than
    /// <paramref name="timeout"/>, 5 s by default). The bytes go to a temp file named by a GUID, deleted after; pdfinfo
    /// runs as a plain process (no shell) whose only argument is that path, never anything the author wrote.
    /// </summary>
    public static int? PdfInfoPages(byte[] pdf, string executable = "pdfinfo", TimeSpan? timeout = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-pages-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, pdf);
            var start = new System.Diagnostics.ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(path);
            using var process = System.Diagnostics.Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(5)).TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            if (process.ExitCode != 0 || !output.Wait(TimeSpan.FromSeconds(1))) return null;
            var m = PdfInfoPagesLine().Match(output.Result);
            return m.Success && int.TryParse(m.Groups["n"].Value, out var n) && n > 0 ? n : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null; // pdfinfo isn't installed, or the temp file couldn't be written
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [GeneratedRegex(@"^Pages:\s+(?<n>\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PdfInfoPagesLine();

    public static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';

    [GeneratedRegex(@"/Type\s*/Pages\b[^<>]*?/Count\s+(?<n>\d+)|/Count\s+(?<n>\d+)[^<>]*?/Type\s*/Pages\b")]
    private static partial Regex CountEntry();

    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageObject();
}
