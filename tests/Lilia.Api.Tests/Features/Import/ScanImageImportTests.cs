using System.Net;
using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Exceptions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// PDF and scan import, 20 Nov 2026: a scanned page (a JPEG or PNG photo of handwritten maths notes) is read by Mathpix.
/// Since the launch bot's handwritten page lost its two sketches (20 Nov 2026), the image goes as a one-page PDF to
/// <c>POST /v3/pdf</c>, which crops figures, rather than to <c>/v3/text</c>, which doesn't ("does not crop sub-regions or
/// upload them to our CDN", docs.mathpix.com/reference/post-v3-text). <see cref="ImagePdf"/> makes that PDF: a JPEG
/// embedded as it is, a PNG losslessly.
///
/// <para>What Mathpix bills is what the plan counts (the owner, 20 Nov 2026): once Mathpix has taken the file (a pdf_id),
/// the read is charged even when the import then fails; before that, nothing is.</para>
///
/// <para>No real Mathpix call: <see cref="MathpixTexZipTests.FakeMathpix"/> answers with the shapes the docs give.</para>
/// </summary>
public class ScanImageImportTests
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>A real image of the given size and format, drawn here.</summary>
    internal static byte[] Image(SkiaSharp.SKEncodedImageFormat format, int width = 40, int height = 30, bool transparent = false)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SkiaSharp.SKColors.Transparent : SkiaSharp.SKColors.White);
            using var pen = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, StrokeWidth = 2 };
            canvas.DrawLine(2, 2, width - 2, height - 2, pen);
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static async Task<(MathpixPdfImportService Service, string Path)> ScanAsync(MathpixTexZipTests.FakeMathpix api, byte[]? image = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, image ?? Png);
        return (MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()), path);
    }

    [Fact]
    public async Task A_scanned_page_goes_to_v3_pdf_as_a_one_page_PDF_with_improve_mathpix_off_and_is_deleted_after()
    {
        var api = new MathpixTexZipTests.FakeMathpix();
        var (service, path) = await ScanAsync(api);
        ImportDocument doc;
        try { doc = await service.ParseImageAsync(path, "image/png"); }
        finally { File.Delete(path); }

        api.Requests.First().Should().Be("POST api.mathpix.com/v3/pdf");
        api.Requests.Should().NotContain(r => r.Contains("/v3/text"));
        api.Requests.Last().Should().Be($"DELETE api.mathpix.com/v3/pdf/{MathpixTexZipTests.PdfId}");
        PdfPages.LooksLikePdf(api.UploadedFile!).Should().BeTrue();
        PdfPages.Exact(api.UploadedFile!).Should().Be(1);
        api.UploadOptions!.RootElement.GetProperty("metadata").GetProperty("improve_mathpix").GetBoolean().Should().BeFalse();
        doc.PageCount.Should().Be(1, "an image is one page for the plan");
        doc.LatexProjectZip.Should().NotBeNull("a scan now has the tex.zip a PDF has, its figures in it");
    }

    [Fact]
    public void A_JPEG_is_embedded_as_it_is_and_a_PNG_losslessly_each_on_one_page()
    {
        var jpeg = Image(SkiaSharp.SKEncodedImageFormat.Jpeg);
        var fromJpeg = ImagePdf.Wrap(jpeg, "image/jpeg")!;
        fromJpeg.AsSpan().IndexOf(jpeg).Should().BeGreaterThan(0, "the JPEG's own bytes, not re-encoded");
        Encoding.Latin1.GetString(fromJpeg).Should().Contain("/DCTDecode").And.Contain("/Width 40 /Height 30");
        PdfPages.Exact(fromJpeg).Should().Be(1);

        var fromPng = ImagePdf.Wrap(Image(SkiaSharp.SKEncodedImageFormat.Png, transparent: true), "image/png")!;
        Encoding.Latin1.GetString(fromPng).Should().Contain("/FlateDecode").And.Contain("/DeviceRGB");
        PdfPages.Exact(fromPng).Should().Be(1);
    }

    [Fact]
    public async Task A_damaged_image_is_refused_in_words_before_Mathpix()
    {
        var api = new MathpixTexZipTests.FakeMathpix();
        var (service, path) = await ScanAsync(api, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        try
        {
            var act = () => service.ParseImageAsync(path, "image/png");
            (await act.Should().ThrowAsync<LiliaValidationException>()).Which.UserMessage.Should().Contain("couldn't read this image");
        }
        finally { File.Delete(path); }

        api.Requests.Should().BeEmpty();
        service.LastCharge.Should().BeNull();
    }

    [Fact]
    public async Task An_image_Mathpix_took_and_then_failed_on_is_still_charged()
    {
        var api = new MathpixTexZipTests.FakeMathpix { PdfStatus = "error" };
        var (service, path) = await ScanAsync(api);
        try
        {
            var act = () => service.ParseImageAsync(path, "image/png");
            await act.Should().ThrowAsync<LiliaExternalServiceException>();
        }
        finally { File.Delete(path); }

        service.LastCharge.Should().NotBeNull("Mathpix had the page");
    }

    [Fact]
    public async Task A_PDF_Mathpix_took_and_then_failed_on_is_still_charged_its_reported_pages()
    {
        var api = new MathpixTexZipTests.FakeMathpix { PdfStatus = "error" };
        var service = MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings());
        var pdf = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4 fake");
        try
        {
            var act = () => service.ParseAsync(pdf);
            await act.Should().ThrowAsync<LiliaExternalServiceException>();
        }
        finally { File.Delete(pdf); }

        service.LastCharge.Should().Be(new Lilia.Import.Interfaces.MathpixCharge(2), "Mathpix had the PDF and said it has 2 pages");
    }

    [Fact]
    public async Task A_PDF_Mathpix_refused_at_the_door_costs_nothing()
    {
        var opts = Options.Create(MathpixTexZipTests.Settings());
        var client = new MathpixClient(new HttpClient(new UnauthorizedMathpix()), opts, NullLogger<MathpixClient>.Instance);
        var service = new MathpixPdfImportService(client, opts, NullLogger<MathpixPdfImportService>.Instance, new LatexProjectExtractor());
        var pdf = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4 fake");
        try
        {
            var act = () => service.ParseAsync(pdf);
            await act.Should().ThrowAsync<LiliaExternalServiceException>();
        }
        finally { File.Delete(pdf); }

        service.LastCharge.Should().BeNull();
    }

    private sealed class UnauthorizedMathpix : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("text", null)]
    [InlineData("pdf", null)]
    public void An_image_is_told_by_its_first_bytes_never_by_its_name(string what, string? expected)
    {
        var bytes = what switch
        {
            "png" => Png,
            "jpeg" => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 },
            "pdf" => Encoding.ASCII.GetBytes("%PDF-1.4 fake"),
            _ => Encoding.UTF8.GetBytes("These are my notes, saved as notes.png."),
        };
        ScanImages.ContentType(bytes).Should().Be(expected);
    }
}
