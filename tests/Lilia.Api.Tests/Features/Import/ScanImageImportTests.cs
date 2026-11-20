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
/// PDF and scan import, 20 Nov 2026: a scanned page (a JPEG or PNG photo of handwritten maths notes) is read by Mathpix's
/// <c>POST /v3/text</c>, the documented route for an image (<c>/v3/pdf</c> takes documents only:
/// docs.mathpix.com/reference/supported-formats), as a multipart upload with <c>options_json</c> asking for Mathpix
/// Markdown with Lilia's delimiters and <c>metadata.improve_mathpix: false</c>. Its Markdown goes through the reader a
/// PDF's Markdown uses.
///
/// <para>What Mathpix bills is what the plan counts (the owner, 20 Nov 2026): once Mathpix has taken the file, a pdf_id
/// or an answer from /v3/text, the read is charged even when the import then fails; before that, nothing is.</para>
///
/// <para>No real Mathpix call: <see cref="MathpixTexZipTests.FakeMathpix"/> answers with the shapes the docs give.</para>
/// </summary>
public class ScanImageImportTests
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<(MathpixPdfImportService Service, string Path)> ScanAsync(MathpixTexZipTests.FakeMathpix api, byte[]? image = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, image ?? Png);
        return (MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()), path);
    }

    [Fact]
    public async Task A_scanned_page_goes_to_v3_text_as_a_file_with_Lilias_delimiters_and_improve_mathpix_off()
    {
        var api = new MathpixTexZipTests.FakeMathpix();
        var (service, path) = await ScanAsync(api);
        try
        {
            await service.ParseImageAsync(path, "image/png");
        }
        finally { File.Delete(path); }

        api.Requests.Should().Equal("POST api.mathpix.com/v3/text");
        api.TextImageType.Should().Be("image/png");
        var options = api.TextOptions!.RootElement;
        options.GetProperty("metadata").GetProperty("improve_mathpix").GetBoolean().Should().BeFalse();
        options.GetProperty("formats").EnumerateArray().Select(f => f.GetString()).Should().Equal("text");
        options.GetProperty("math_inline_delimiters").EnumerateArray().Select(f => f.GetString()).Should().Equal("$", "$");
        options.GetProperty("math_display_delimiters").EnumerateArray().Select(f => f.GetString()).Should().Equal("$$", "$$");
        options.TryGetProperty("src", out _).Should().BeFalse("the image is uploaded, not sent as base64, which Mathpix caps at 2 MB");
    }

    [Fact]
    public async Task A_scanned_pages_Markdown_becomes_a_heading_text_and_a_display_equation_counted_as_one_page()
    {
        var api = new MathpixTexZipTests.FakeMathpix();
        var (service, path) = await ScanAsync(api);
        ImportDocument doc;
        try { doc = await service.ParseImageAsync(path, "image/png"); }
        finally { File.Delete(path); }

        doc.PageCount.Should().Be(1);
        doc.LatexProjectZip.Should().BeNull("/v3/text answers Markdown only");
        doc.Elements.OfType<ImportHeading>().Should().ContainSingle(h => h.Text.Contains("Gaussian integral"));
        doc.Elements.OfType<ImportEquation>().Should().ContainSingle(e => (e.LatexContent ?? "").Contains("\\sqrt{\\frac{\\pi}{\\alpha}}"));
        service.LastCharge.Should().Be(new Lilia.Import.Interfaces.MathpixCharge(1));
    }

    [Fact]
    public async Task An_image_Mathpix_answered_with_an_error_is_refused_in_words_and_still_charged_as_one_page()
    {
        var api = new MathpixTexZipTests.FakeMathpix
        {
            TextAnswer = () => MathpixTexZipTests.FakeMathpix.Json(new
            {
                request_id = "x",
                error = "Content not found",
                error_info = new { id = "image_no_content", message = "Content not found" },
            }),
        };
        var (service, path) = await ScanAsync(api);
        try
        {
            var act = () => service.ParseImageAsync(path, "image/png");
            (await act.Should().ThrowAsync<LiliaExternalServiceException>()).Which.UserMessage.Should().Contain("couldn't read this image");
        }
        finally { File.Delete(path); }

        service.LastCharge.Should().Be(new Lilia.Import.Interfaces.MathpixCharge(1), "Mathpix answered, so it bills the image");
    }

    [Fact]
    public async Task An_image_Mathpix_refused_at_the_door_costs_nothing()
    {
        var api = new MathpixTexZipTests.FakeMathpix { TextAnswer = () => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
        var (service, path) = await ScanAsync(api);
        try
        {
            var act = () => service.ParseImageAsync(path, "image/png");
            await act.Should().ThrowAsync<LiliaExternalServiceException>();
        }
        finally { File.Delete(path); }

        service.LastCharge.Should().BeNull();
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
