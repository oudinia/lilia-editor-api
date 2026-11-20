using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Exceptions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// PDF import via tex.zip, 19 Nov 2026. Mathpix is asked for its LaTeX output (tex.zip) with the Markdown; when the
/// tex.zip is ready it is imported as an uploaded Overleaf zip is, and when it fails or takes too long the Markdown
/// is used, as before. Every upload asks Mathpix not to keep or use the file (<c>metadata.improve_mathpix: false</c>),
/// and the PDF is deleted at Mathpix (<c>DELETE /v3/pdf/{pdf_id}</c>) once Lilia has the result.
///
/// <para>No real Mathpix call: the API is <see cref="FakeMathpix"/>, a handler answering with the shapes Mathpix's docs
/// give (docs.mathpix.com/reference/post-v3-pdf, /reference/shared-types), and the tex.zip is built here.</para>
/// </summary>
public class MathpixTexZipTests
{
    internal const string PdfId = "2026_11_19_0a1b2c3d4e5f";

    /// <summary>
    /// A tex.zip shaped as Mathpix's docs describe it: LaTeX whose figures are
    /// <c>\includegraphics[max width=\textwidth]{image-name}</c>, the images in an <c>images</c> folder (the tex.zip
    /// conversion option <c>imagesFolder</c>, default "images").
    /// </summary>
    internal static byte[] TexZip()
    {
        const string tex = """
            \documentclass[10pt]{article}
            \usepackage[utf8]{inputenc}
            \usepackage{amsmath}
            \usepackage{graphicx}
            \usepackage[export]{adjustbox}
            \graphicspath{ {./images/} }

            \begin{document}
            \section{Introduction}
            Attention is all you need, read from the tex.zip.

            \begin{equation*}
            E=m c^{2}
            \end{equation*}

            \begin{figure}[h]
            \begin{center}
            \includegraphics[max width=\textwidth]{2026_11_19_0a1b2c3d4e5f-1}
            \caption{The model.}
            \end{center}
            \end{figure}

            \end{document}
            """;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry($"{PdfId}/{PdfId}.tex").Open(), Encoding.UTF8))
                w.Write(tex);
            using (var s = zip.CreateEntry($"{PdfId}/images/{PdfId}-1.jpg").Open())
                s.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9]);
        }
        return ms.ToArray();
    }

    internal const string Markdown = """
        # Introduction

        Attention is all you need, read from the Markdown.

        ![](https://cdn.mathpix.com/cropped/2026_11_19_0a1b2c3d4e5f-1.jpg?height=200&width=300&top_left_y=10&top_left_x=20)
        """;

    /// <summary>
    /// Mathpix's v3 PDF API as its docs describe it. Records every request, in order, and the upload's
    /// <c>options_json</c>. <see cref="TexZipStatuses"/> are the tex.zip conversion statuses the converter answers in
    /// turn (the last one repeats); <see cref="TexZipDownload"/> is what the tex.zip download answers.
    /// </summary>
    internal sealed class FakeMathpix : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public JsonDocument? UploadOptions { get; private set; }
        public string PdfStatus { get; init; } = "completed";
        public Queue<string> TexZipStatuses { get; init; } = new(["completed"]);
        public Func<HttpResponseMessage> TexZipDownload { get; init; } = () => Zip(TexZip());
        /// <summary>The file uploaded to <c>POST /v3/pdf</c>: a scan's one-page PDF since 20 Nov 2026.</summary>
        public byte[]? UploadedFile { get; private set; }
        /// <summary>What the Markdown download answers (figures in imported scans, 20 Nov 2026).</summary>
        public string MarkdownBody { get; init; } = Markdown;
        /// <summary>Figure requests that carried the Mathpix key: must stay 0 (security hotfix #673).</summary>
        public int CdnRequestsWithKey { get; private set; }
        /// <summary>What a cropped figure on cdn.mathpix.com answers: a JPEG by default.</summary>
        public Func<HttpResponseMessage> CdnFigure { get; init; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]),
        };
        /// <summary>How long the Markdown download takes: a slow Mathpix conversion, for the deadline.</summary>
        public TimeSpan MarkdownDelay { get; init; } = TimeSpan.Zero;

        private static HttpResponseMessage Zip(byte[] bytes) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/zip") } },
        };

        internal static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {request.RequestUri.Host}{path}");

            if (request.Method == HttpMethod.Post && path == "/v3/pdf")
            {
                var form = (MultipartFormDataContent)request.Content!;
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name == "options_json") UploadOptions = JsonDocument.Parse(await part.ReadAsStringAsync(ct));
                    if (name == "file") UploadedFile = await part.ReadAsByteArrayAsync(ct);
                }
                return Json(new { pdf_id = PdfId });
            }
            if (request.Method == HttpMethod.Get && path == $"/v3/pdf/{PdfId}")
                return Json(new { status = PdfStatus, num_pages = 2, num_pages_completed = 2, percent_done = 100.0, error = PdfStatus == "error" ? "PDF is encrypted" : null });
            if (request.Method == HttpMethod.Get && path == $"/v3/pdf/{PdfId}.md")
            {
                if (MarkdownDelay > TimeSpan.Zero) await Task.Delay(MarkdownDelay, ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MarkdownBody) };
            }
            if (request.Method == HttpMethod.Get && path == $"/v3/converter/{PdfId}")
            {
                var texStatus = TexZipStatuses.Count > 1 ? TexZipStatuses.Dequeue() : TexZipStatuses.Peek();
                object tex = texStatus == "error"
                    ? new { status = "error", error_info = new { id = "conversion_error", error = "Conversion failed" } }
                    : new { status = texStatus };
                return Json(new { status = "completed", conversion_status = new Dictionary<string, object> { ["md"] = new { status = "completed" }, ["tex.zip"] = tex } });
            }
            if (request.Method == HttpMethod.Get && path == $"/v3/pdf/{PdfId}.tex.zip")
                return TexZipDownload();
            if (request.Method == HttpMethod.Get && request.RequestUri.Host == "cdn.mathpix.com")
            {
                if (request.Headers.Contains("app_key")) CdnRequestsWithKey++;
                return CdnFigure();
            }
            if (request.Method == HttpMethod.Delete && path == $"/v3/pdf/{PdfId}")
                return Json(new { status = PdfStatus, num_pages = 2 });

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
    }

    internal static MathpixOptions Settings(Action<MathpixOptions>? change = null)
    {
        var o = new MathpixOptions { AppKey = "test-key", PollIntervalMs = 10, TimeoutSeconds = 5, TexZipTimeoutSeconds = 5 };
        change?.Invoke(o);
        return o;
    }

    internal static MathpixPdfImportService Service(FakeMathpix api, MathpixOptions options)
    {
        var opts = Options.Create(options);
        var client = new MathpixClient(new HttpClient(api), opts, NullLogger<MathpixClient>.Instance);
        // Figures go through their own client since the security hotfix (20 Nov 2026): the same fake serves them here.
        client.ImageHttp = new HttpClient(api);
        return new MathpixPdfImportService(client, opts, NullLogger<MathpixPdfImportService>.Instance, new LatexProjectExtractor());
    }

    private static async Task<ImportDocument> ImportAsync(FakeMathpix api, MathpixOptions? options = null)
    {
        var pdf = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4 fake");
        try
        {
            return await Service(api, options ?? Settings()).ParseAsync(pdf);
        }
        finally
        {
            File.Delete(pdf);
        }
    }

    // ── The upload ────────────────────────────────────────────────────

    [Fact]
    public async Task The_upload_asks_for_the_tex_zip_with_the_Markdown()
    {
        var api = new FakeMathpix();
        await ImportAsync(api);

        var formats = api.UploadOptions!.RootElement.GetProperty("conversion_formats");
        formats.GetProperty("md").GetBoolean().Should().BeTrue();
        formats.GetProperty("tex.zip").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_upload_tells_Mathpix_not_to_keep_or_use_the_file()
    {
        var api = new FakeMathpix();
        await ImportAsync(api);

        api.UploadOptions!.RootElement.GetProperty("metadata").GetProperty("improve_mathpix").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task With_the_tex_zip_switched_off_the_upload_asks_for_Markdown_only_and_the_converter_is_not_asked()
    {
        var api = new FakeMathpix();
        var doc = await ImportAsync(api, Settings(o => o.RequestTexZip = false));

        api.UploadOptions!.RootElement.GetProperty("conversion_formats").TryGetProperty("tex.zip", out _).Should().BeFalse();
        api.Requests.Should().NotContain(r => r.Contains("/v3/converter/"));
        doc.LatexProjectZip.Should().BeNull();
        doc.Elements.Should().Contain(e => e is ImportHeading);
    }

    // ── tex.zip ready ─────────────────────────────────────────────────

    [Fact]
    public async Task A_ready_tex_zip_comes_back_as_the_LaTeX_project_to_import_with_its_figure_file()
    {
        // Converting at first, then ready: the converter is asked until it says completed.
        var api = new FakeMathpix { TexZipStatuses = new(["processing", "processing", "completed"]) };
        var doc = await ImportAsync(api);

        doc.LatexProjectZip.Should().NotBeNull();
        doc.LatexSource.Should().Contain(@"\section{Introduction}").And.Contain("read from the tex.zip");
        // The Markdown isn't read into blocks, and its images aren't fetched: the project carries the figure.
        doc.Elements.Should().BeEmpty();
        api.Requests.Should().NotContain(r => r.Contains("cdn.mathpix.com"));
        new LatexProjectExtractor().Extract(doc.LatexProjectZip!).Files
            .Should().ContainSingle(f => f.Kind == LatexProjectFileKinds.Image && f.Path.EndsWith($"{PdfId}-1.jpg"));
        // The pages Mathpix read: what the plan's PDF pages count, unchanged.
        doc.PageCount.Should().Be(2);
        api.Requests.Count(r => r.EndsWith($"/v3/converter/{PdfId}")).Should().Be(3);
        api.Requests.Should().Contain($"GET api.mathpix.com/v3/pdf/{PdfId}.tex.zip");
    }

    // ── tex.zip not usable: the Markdown ──────────────────────────────

    [Fact]
    public async Task A_failed_tex_zip_conversion_falls_back_to_the_Markdown()
    {
        var api = new FakeMathpix { TexZipStatuses = new(["error"]) };
        var doc = await ImportAsync(api);

        doc.LatexProjectZip.Should().BeNull();
        doc.LatexSource.Should().BeNull();
        doc.Elements.OfType<ImportHeading>().Should().ContainSingle(h => h.Text == "Introduction");
        doc.Elements.OfType<ImportParagraph>().Should().Contain(p => p.Text.Contains("read from the Markdown"));
        doc.Elements.OfType<ImportImage>().Should().ContainSingle().Which.Data.Should().NotBeEmpty();
        api.Requests.Should().NotContain(r => r.EndsWith(".tex.zip"));
    }

    [Fact]
    public async Task A_tex_zip_still_converting_after_the_wait_falls_back_to_the_Markdown()
    {
        var api = new FakeMathpix { TexZipStatuses = new(["processing"]) };
        var doc = await ImportAsync(api, Settings(o => o.TexZipTimeoutSeconds = 1));

        doc.LatexProjectZip.Should().BeNull();
        doc.Elements.OfType<ImportHeading>().Should().ContainSingle(h => h.Text == "Introduction");
    }

    [Fact]
    public async Task A_tex_zip_download_that_is_not_ready_falls_back_to_the_Markdown()
    {
        // 202: "a conversion format whose conversion is still running" (docs.mathpix.com/reference/post-v3-pdf).
        var api = new FakeMathpix { TexZipDownload = () => new HttpResponseMessage(HttpStatusCode.Accepted) };
        var doc = await ImportAsync(api);

        doc.LatexProjectZip.Should().BeNull();
        doc.Elements.Should().Contain(e => e is ImportHeading);
    }

    [Fact]
    public async Task A_tex_zip_without_a_LaTeX_document_in_it_falls_back_to_the_Markdown()
    {
        var api = new FakeMathpix
        {
            TexZipDownload = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not a zip")) },
        };
        var doc = await ImportAsync(api);

        doc.LatexProjectZip.Should().BeNull();
        doc.Elements.Should().Contain(e => e is ImportHeading);
    }

    // ── The deadline: the import runs inside the web request, which Cloudflare cuts at 100 s ──

    [Fact]
    public void The_tex_zip_wait_defaults_to_25_seconds_and_ends_80_seconds_after_the_upload()
    {
        var defaults = new MathpixOptions();

        defaults.TexZipTimeoutSeconds.Should().Be(25);
        defaults.TexZipDeadlineSeconds.Should().Be(80);
    }

    [Fact]
    public async Task A_Markdown_conversion_that_used_up_the_deadline_goes_straight_to_the_Markdown_without_asking_for_the_tex_zip()
    {
        // The deadline (1 s here, 80 s in production) is spent by the time the Markdown arrives.
        var api = new FakeMathpix { MarkdownDelay = TimeSpan.FromMilliseconds(1300) };
        var doc = await ImportAsync(api, Settings(o => o.TexZipDeadlineSeconds = 1));

        api.Requests.Should().NotContain(r => r.Contains("/v3/converter/") || r.EndsWith(".tex.zip"));
        doc.LatexProjectZip.Should().BeNull();
        doc.Elements.OfType<ImportHeading>().Should().ContainSingle(h => h.Text == "Introduction");
        api.Requests.Last().Should().Be($"DELETE api.mathpix.com/v3/pdf/{PdfId}");
    }

    [Fact]
    public async Task The_tex_zip_wait_stops_at_the_deadline_even_when_its_own_timeout_is_longer()
    {
        // tex.zip never finishes; its own timeout (25 s) is longer than what is left of a 2 s deadline.
        var api = new FakeMathpix { TexZipStatuses = new(["processing"]) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var doc = await ImportAsync(api, Settings(o => { o.TexZipTimeoutSeconds = 25; o.TexZipDeadlineSeconds = 2; }));
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        api.Requests.Should().Contain(r => r.EndsWith($"/v3/converter/{PdfId}"));
        doc.LatexProjectZip.Should().BeNull();
        doc.Elements.OfType<ImportHeading>().Should().ContainSingle(h => h.Text == "Introduction");
    }

    // ── Deleting at Mathpix ───────────────────────────────────────────

    [Fact]
    public async Task The_PDF_is_deleted_at_Mathpix_once_the_tex_zip_is_downloaded()
    {
        var api = new FakeMathpix();
        await ImportAsync(api);

        api.Requests.Last().Should().Be($"DELETE api.mathpix.com/v3/pdf/{PdfId}");
        api.Requests.Count(r => r.StartsWith("DELETE")).Should().Be(1);
        api.Requests.IndexOf($"GET api.mathpix.com/v3/pdf/{PdfId}.tex.zip").Should().BeLessThan(api.Requests.Count - 1);
    }

    [Fact]
    public async Task On_the_Markdown_fallback_the_PDF_is_deleted_after_its_images_are_fetched()
    {
        // The delete removes the page images behind the Markdown's cdn.mathpix.com links: they are fetched first.
        var api = new FakeMathpix { TexZipStatuses = new(["error"]) };
        await ImportAsync(api);

        var image = api.Requests.FindIndex(r => r.StartsWith("GET cdn.mathpix.com"));
        var delete = api.Requests.FindIndex(r => r.StartsWith("DELETE"));
        image.Should().BeGreaterThanOrEqualTo(0);
        delete.Should().Be(api.Requests.Count - 1).And.BeGreaterThan(image);
    }

    [Fact]
    public async Task A_PDF_Mathpix_could_not_read_is_still_deleted_there()
    {
        var api = new FakeMathpix { PdfStatus = "error" };

        var act = () => ImportAsync(api);

        await act.Should().ThrowAsync<LiliaExternalServiceException>();
        api.Requests.Last().Should().Be($"DELETE api.mathpix.com/v3/pdf/{PdfId}");
    }

    [Fact]
    public async Task With_deleting_switched_off_nothing_is_deleted_at_Mathpix()
    {
        var api = new FakeMathpix();
        await ImportAsync(api, Settings(o => o.DeleteAfterImport = false));

        api.Requests.Should().NotContain(r => r.StartsWith("DELETE"));
    }
}
