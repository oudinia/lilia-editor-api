using System.Diagnostics;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>The pages of one chunk: the file's first and last page in it, from 1, inclusive.</summary>
public readonly record struct PageRange(int Index, int First, int Last)
{
    public int Count => Last - First + 1;
}

/// <summary>Cuts a PDF on disk into chunk PDFs on disk.</summary>
public interface IPdfSplitter
{
    /// <summary>
    /// Writes each range of <paramref name="sourcePath"/> to its own PDF in <paramref name="outputDir"/> and returns the
    /// paths, in the ranges' order. Throws when the PDF can't be split.
    /// </summary>
    Task<IReadOnlyList<string>> SplitAsync(string sourcePath, IReadOnlyList<PageRange> ranges, string outputDir, CancellationToken ct);
}

/// <summary>
/// Splitting for bulk import (21 Nov 2026). In process with PdfPig (Apache-2.0; 0.1.16, Aug 2026, builds for net8.0
/// and runs on net10.0): the source is opened from its file (a stream, never read whole into memory) once, and each chunk
/// is written straight to its own file. PDFsharp, the other candidate, moved to AGPL-3.0 with 6.5.0 (14 Oct 2026). When
/// PdfPig can't read the PDF or a chunk comes out with the wrong page count, poppler's <c>pdfseparate</c> and
/// <c>pdfunite</c> (installed on the server) do it: plain processes, no shell, a timeout, arguments that are paths
/// Lilia chose and page numbers.
/// </summary>
public sealed class PdfSplitter(BulkImportOptions options, ILogger<PdfSplitter> logger) : IPdfSplitter
{
    /// <summary>The ranges of a PDF of <paramref name="pages"/> pages cut every <paramref name="perChunk"/> pages.</summary>
    public static IReadOnlyList<PageRange> Ranges(int pages, int perChunk)
    {
        if (pages <= 0) return [];
        perChunk = Math.Max(1, perChunk);
        var ranges = new List<PageRange>((pages + perChunk - 1) / perChunk);
        for (int first = 1, i = 0; first <= pages; first += perChunk, i++)
            ranges.Add(new PageRange(i, first, Math.Min(pages, first + perChunk - 1)));
        return ranges;
    }

    public async Task<IReadOnlyList<string>> SplitAsync(string sourcePath, IReadOnlyList<PageRange> ranges, string outputDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);
        try
        {
            var paths = SplitWithPdfPig(sourcePath, ranges, outputDir, ct);
            if (paths.Zip(ranges).All(p => CountPages(p.First) == p.Second.Count)) return paths;
            logger.LogWarning("[BulkImport] PdfPig's chunks of {Source} have the wrong pages; splitting with poppler", Path.GetFileName(sourcePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[BulkImport] PdfPig couldn't split {Source}; splitting with poppler", Path.GetFileName(sourcePath));
        }
        foreach (var f in Directory.EnumerateFiles(outputDir)) TryDelete(f);
        return await SplitWithPopplerAsync(sourcePath, ranges, outputDir, ct);
    }

    private static List<string> SplitWithPdfPig(string sourcePath, IReadOnlyList<PageRange> ranges, string outputDir, CancellationToken ct)
    {
        var paths = new List<string>(ranges.Count);
        using var source = File.OpenRead(sourcePath);
        using var document = PdfDocument.Open(source, new ParsingOptions { UseLenientParsing = true, ClipPaths = false });
        foreach (var range in ranges)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(outputDir, $"chunk-{range.Index:D4}.pdf");
            using (var output = File.Create(path))
            using (var builder = new PdfDocumentBuilder(output, disposeStream: false))
            {
                for (var page = range.First; page <= range.Last; page++) builder.AddPage(document, page);
                builder.Build();
            }
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>The pages of a PDF on disk by PdfPig, or -1.</summary>
    internal static int CountPages(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = PdfDocument.Open(stream, new ParsingOptions { UseLenientParsing = true });
            return document.NumberOfPages;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    internal async Task<IReadOnlyList<string>> SplitWithPopplerAsync(string sourcePath, IReadOnlyList<PageRange> ranges, string outputDir, CancellationToken ct)
    {
        var paths = new List<string>(ranges.Count);
        var pagesDir = Path.Combine(outputDir, "pages");
        foreach (var range in ranges)
        {
            // One chunk's pages at a time, so the disk holds at most one chunk's single pages besides the chunks.
            Directory.CreateDirectory(pagesDir);
            await RunAsync("pdfseparate", ["-f", range.First.ToString(), "-l", range.Last.ToString(), sourcePath, Path.Combine(pagesDir, "p-%d.pdf")], ct);
            var path = Path.Combine(outputDir, $"chunk-{range.Index:D4}.pdf");
            var pages = Enumerable.Range(range.First, range.Count).Select(p => Path.Combine(pagesDir, $"p-{p}.pdf")).ToList();
            if (pages.Count == 1) File.Copy(pages[0], path, overwrite: true);
            else await RunAsync("pdfunite", [.. pages, path], ct);
            Directory.Delete(pagesDir, recursive: true);
            paths.Add(path);
        }
        return paths;
    }

    private async Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, options.SplitTimeoutSeconds)));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        _ = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{executable} took longer than {options.SplitTimeoutSeconds} s.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{executable} failed ({process.ExitCode}): {(await stderr).Trim()}");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
