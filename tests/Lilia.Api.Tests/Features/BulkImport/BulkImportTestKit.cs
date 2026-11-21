using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>What the bulk import tests share: PDFs made here, a fake Mathpix, and a queue the test runs by hand.</summary>
internal static class BulkImportTestKit
{
    /// <summary>A PDF of <paramref name="pages"/> pages, each saying "page N end", so a chunk shows which pages it holds.</summary>
    public static byte[] Pdf(int pages, string salt = "")
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 1; i <= pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText($"page {i} end", 12, new PdfPoint(40, 700), font);
            if (salt.Length > 0) page.AddText($"id {salt}", 10, new PdfPoint(40, 650), font);
        }
        return builder.Build();
    }

    /// <summary>The page numbers a PDF on disk says it holds ("page N" on each), in order.</summary>
    public static List<int> PageNumbers(string path)
    {
        using var document = PdfDocument.Open(path);
        return document.GetPages()
            .Select(p => System.Text.RegularExpressions.Regex.Match(p.Text, @"page\s*(\d+)\s*end"))
            .Select(m => m.Success ? int.Parse(m.Groups[1].Value) : -1)
            .ToList();
    }
}

/// <summary>Records what the slice sends; the test runs each message through the processor when and in the order it wants.</summary>
internal sealed class RecordingQueue : IBulkImportQueue
{
    public ConcurrentQueue<(object Message, TimeSpan? Delay)> Sent { get; } = new();

    public Task SendAsync(object message, TimeSpan? delay = null)
    {
        Sent.Enqueue((message, delay));
        return Task.CompletedTask;
    }

    /// <summary>Takes every message of type <typeparamref name="T"/> sent so far, leaving the others.</summary>
    public List<T> Take<T>()
    {
        var taken = new List<T>();
        var keep = new List<(object, TimeSpan?)>();
        while (Sent.TryDequeue(out var item))
        {
            if (item.Message is T t) taken.Add(t);
            else keep.Add(item);
        }
        foreach (var k in keep) Sent.Enqueue(k);
        return taken;
    }
}

/// <summary>
/// Mathpix's PDF API as <see cref="IMathpixClient"/> sees it, no network. A submitted chunk is told by its pages'
/// text ("page N"); its status goes split (0 pages read), split (some), completed; its tex.zip says which pages it
/// holds and defines a macro of its own. Pages listed in <see cref="FailFirstPages"/> answer "error".
/// </summary>
internal sealed class FakeMathpixClient : IMathpixClient
{
    private sealed record Submitted(int First, int Last, int Pages)
    {
        public int Polls;
    }

    private readonly ConcurrentDictionary<string, Submitted> _pdfs = new();
    private int _submits, _deletes;

    public int Submits => _submits;
    public int Deletes => _deletes;
    public ConcurrentBag<int> FailFirstPages { get; } = new();

    /// <summary>Chunks (by first page) whose reading has a figure: <c>\includegraphics{fig-N}</c> and its image in the zip.</summary>
    public ConcurrentBag<int> FigureFirstPages { get; } = new();

    /// <summary>A 1×1 PNG, the figures' image.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>Called on each status poll with the chunk's first page and the pages it then reports read.</summary>
    public Func<int, int, Task>? OnPoll { get; set; }

    public Task<string> SubmitPdfAsync(string filePath, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _submits);
        var pages = BulkImportTestKit.PageNumbers(filePath);
        var id = $"fake_{Guid.NewGuid():N}";
        _pdfs[id] = new Submitted(pages.First(), pages.Last(), pages.Count);
        return Task.FromResult(id);
    }

    public async Task<MathpixPdfStatus> GetStatusAsync(string pdfId, CancellationToken ct = default)
    {
        var pdf = _pdfs[pdfId];
        if (FailFirstPages.Contains(pdf.First))
            return new MathpixPdfStatus { Status = "error", Error = "PDF is unreadable", NumPages = pdf.Pages };
        var poll = Interlocked.Increment(ref pdf.Polls);
        var completed = poll switch { 1 => 0, 2 => Math.Min(4, pdf.Pages), _ => pdf.Pages };
        if (OnPoll is { } onPoll) await onPoll(pdf.First, completed);
        return new MathpixPdfStatus
        {
            Status = poll >= 3 ? "completed" : "split",
            NumPages = pdf.Pages,
            NumPagesCompleted = completed,
            PercentDone = 100.0 * completed / pdf.Pages,
        };
    }

    public Task<byte[]> GetTexZipAsync(string pdfId, CancellationToken ct = default, TimeSpan? maxWait = null)
    {
        var pdf = _pdfs[pdfId];
        var figure = FigureFirstPages.Contains(pdf.First);
        var tex = string.Join("\n",
            @"\documentclass[11pt]{article}",
            @"\usepackage{amsmath}",
            @"\usepackage{graphicx}",
            @"\graphicspath{ {./images/} }",
            @"\newcommand{\RR}{\mathbb{R}}",
            @"\newcommand{\From" + Word(pdf.First) + "}{p" + pdf.First + "}",
            "",
            @"\begin{document}",
            $"Text of pages {pdf.First} to {pdf.Last}.",
            "",
            figure ? @"\begin{center}" + "\n" + @"\includegraphics[max width=\textwidth]{fig-" + pdf.First + "}\n" + @"\end{center}" : "",
            "",
            @"\end{document}");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry($"{pdfId}/{pdfId}.tex").Open(), Encoding.UTF8))
                w.Write(tex);
            if (figure)
                using (var image = zip.CreateEntry($"{pdfId}/images/fig-{pdf.First}.png").Open())
                    image.Write(Png);
        }
        return Task.FromResult(ms.ToArray());
    }

    /// <summary>A macro name for a page number (macro names are letters only).</summary>
    public static string Word(int n) => new(n.ToString().Select(c => (char)('A' + (c - '0'))).ToArray());

    public Task<bool> DeletePdfAsync(string pdfId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _deletes);
        return Task.FromResult(true);
    }

    /// <summary>
    /// The lines.json answered for a chunk, by its first page: a test's fixture, or (by default) every page read with
    /// confidence 0.95. <see cref="LinesFail"/> makes it answer an error.
    /// </summary>
    public Func<int, int, string>? LinesJson { get; set; }
    public bool LinesFail { get; set; }
    public int LinesCalls => _lines;
    private int _lines;

    public Task<string> GetLinesJsonAsync(string pdfId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _lines);
        if (LinesFail) throw new HttpRequestException("Mathpix lines.json answered 500.");
        var pdf = _pdfs[pdfId];
        return Task.FromResult(LinesJson is { } make ? make(pdf.First, pdf.Pages) : Lines(pdf.Pages, _ => 0.95));
    }

    /// <summary>A lines.json of <paramref name="pages"/> pages (numbered from 1, as Mathpix numbers a chunk's), two lines each.</summary>
    public static string Lines(int pages, Func<int, double> rateOfPage) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            pages = Enumerable.Range(1, pages).Select(p => new
            {
                image_id = $"fake-{p}",
                page = p,
                page_width = 1700,
                page_height = 2200,
                lines = new object[]
                {
                    new { type = "text", text = $"Line one of page {p}", confidence = rateOfPage(p), confidence_rate = rateOfPage(p) },
                    new { type = "math", text = "x^2 + y^2 = z^2", confidence = rateOfPage(p), confidence_rate = rateOfPage(p) },
                },
            }),
        });

    public Task<string> WaitForCompletionAsync(string pdfId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<byte[]> GetImageAsync(string imageUrl, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Records what the slice tells the author's pages: each file's progress, and each preview change.</summary>
internal sealed class RecordingNotifier : IBulkImportNotifier
{
    public ConcurrentQueue<Lilia.Api.Features.BulkImport.Contracts.BulkImportFileDto> Progress { get; } = new();
    public ConcurrentQueue<(Guid BatchId, Guid FileId, int ChunkIndex)> PreviewChanges { get; } = new();

    public Task FileChangedAsync(string userId, Guid batchId, Lilia.Api.Features.BulkImport.Contracts.BulkImportFileDto file, bool force)
    {
        Progress.Enqueue(file);
        return Task.CompletedTask;
    }

    public Task PreviewChangedAsync(string userId, Guid batchId, Guid fileId, int chunkIndex)
    {
        PreviewChanges.Enqueue((batchId, fileId, chunkIndex));
        return Task.CompletedTask;
    }
}

/// <summary>The compile after a file is done, faked (no TeX): records each document, answers <see cref="Result"/>.</summary>
internal sealed class FakeBulkCompiler : Lilia.Api.Services.IBulkImportCompiler
{
    public ConcurrentBag<Guid> Compiled { get; } = new();
    public Lilia.Api.Services.BulkImportCompileResult Result { get; set; } = new(true, "typst", null);
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(15);

    public async Task<Lilia.Api.Services.BulkImportCompileResult> CompileAsync(Guid documentId, CancellationToken ct = default)
    {
        Compiled.Add(documentId);
        await Task.Delay(Delay, ct);
        return Result;
    }
}
