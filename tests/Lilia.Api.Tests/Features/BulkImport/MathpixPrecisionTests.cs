using System.Net;
using System.Text;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// The precision score (21 Nov 2026) from Mathpix's lines.json, as docs.mathpix.com/reference/post-v3-pdf describes it
/// ("lines data"): <c>pages[]</c> with <c>page</c> and <c>lines[]</c>, each line with <c>text</c>, <c>text_display</c>,
/// <c>confidence</c> and <c>confidence_rate</c>. No network: the fixture is written here, and the client's request is
/// answered by a handler.
/// </summary>
public class MathpixPrecisionTests
{
    /// <summary>
    /// Four pages of one chunk, numbered from 1 as Mathpix numbers a submitted PDF's:
    /// <list type="bullet">
    /// <item>page 1: 10 characters at 0.90 and 30 at 0.98 → (9 + 29.4) / 40 = 0.96, lowest 0.90;</item>
    /// <item>page 2: 7 at 0.50 and 13 at 0.70, plus a figure with no text (left out) → (3.5 + 9.1) / 20 = 0.63: low;</item>
    /// <item>page 3: a line with <c>confidence</c> only (0.85), and one with no score at all (left out) → 0.85;</item>
    /// <item>page 4: no lines → no score.</item>
    /// </list>
    /// </summary>
    internal const string Fixture = """
        {
          "pages": [
            {
              "image_id": "2026_11_22_abc-1", "page": 1, "page_width": 1700, "page_height": 2200,
              "page_confidence": { "confidence": 0.9, "text_confidence": 0.9, "layout_confidence": 0.95, "method": "prototype_min_v1" },
              "lines": [
                { "id": "a1", "type": "text", "text": "ABCDEFGHIJ", "text_display": "ABCDEFGHIJ", "is_printed": true, "is_handwritten": false, "conversion_output": true, "confidence": 0.41, "confidence_rate": 0.90 },
                { "id": "a2", "type": "text", "text": "Let f be continuous on [0, 1].", "text_display": "Let $f$ be continuous on $[0,1]$.", "is_printed": true, "is_handwritten": false, "conversion_output": true, "confidence": 0.55, "confidence_rate": 0.98 }
              ]
            },
            {
              "image_id": "2026_11_22_abc-2", "page": 2, "page_width": 1700, "page_height": 2200,
              "lines": [
                { "id": "b1", "type": "math", "text": "x^2+y^2", "confidence": 0.01, "confidence_rate": 0.50 },
                { "id": "b2", "type": "text", "text": "ABCDEFGHIJKLM", "confidence": 0.02, "confidence_rate": 0.70 },
                { "id": "b3", "type": "diagram", "text": "", "text_display": "", "confidence": 0.1, "confidence_rate": 0.1 }
              ]
            },
            {
              "image_id": "2026_11_22_abc-3", "page": 3, "page_width": 1700, "page_height": 2200,
              "lines": [
                { "id": "c1", "type": "text", "text": "Proof.", "confidence": 0.85 },
                { "id": "c2", "type": "text", "text": "QED", "confidence": null, "confidence_rate": null }
              ]
            },
            { "image_id": "2026_11_22_abc-4", "page": 4, "page_width": 1700, "page_height": 2200, "lines": [] }
          ]
        }
        """;

    [Fact]
    public void Each_page_is_the_character_weighted_mean_of_its_lines_and_its_lowest_line()
    {
        var pages = MathpixPrecision.FromLinesJson(Fixture, pageOffset: 10, lowThreshold: 0.80);

        pages.Should().Equal(
            new PagePrecision(11, 0.96, 0.90, 2, 40, false),
            new PagePrecision(12, 0.63, 0.50, 2, 20, true),
            new PagePrecision(13, 0.85, 0.85, 1, 6, false));
        MathpixPrecision.Mean(pages).Should().Be(0.813, "each page once: (0.96 + 0.63 + 0.85) / 3");
        MathpixPrecision.LowPageWarning(pages[1]).Should().Be("Page 12: the reader was unsure (precision 0.63). Check it against the scan");
    }

    [Fact]
    public void The_threshold_decides_which_pages_are_low()
    {
        MathpixPrecision.FromLinesJson(Fixture, 0, 0.90).Where(p => p.Low).Select(p => p.Page).Should().Equal(2, 3);
        MathpixPrecision.FromLinesJson(Fixture, 0, 0.50).Should().NotContain(p => p.Low);
    }

    [Fact]
    public void Stored_pages_read_back_and_bad_data_gives_none()
    {
        var pages = MathpixPrecision.FromLinesJson(Fixture);
        MathpixPrecision.Parse(MathpixPrecision.Serialize(pages)).Should().Equal(pages);
        MathpixPrecision.Serialize(pages).Should().Contain("\"page\":1").And.Contain("\"low\":true");
        MathpixPrecision.Parse(null).Should().BeEmpty();
        MathpixPrecision.Parse("not json").Should().BeEmpty();
        MathpixPrecision.FromLinesJson("""{"pages": "no"}""").Should().BeEmpty();
        MathpixPrecision.FromLinesJson("[]").Should().BeEmpty();
        MathpixPrecision.Mean([]).Should().BeNull();
    }

    private sealed class LinesApi(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(string Url, bool Keyed)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Contains("app_key")));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task The_client_asks_api_mathpix_com_with_its_key_whatever_the_base_url()
    {
        var api = new LinesApi(HttpStatusCode.OK, Fixture);
        var options = Options.Create(new MathpixOptions { AppKey = "test-key", BaseUrl = "https://elsewhere.example" });
        var client = new MathpixClient(new HttpClient(api), options, NullLogger<MathpixClient>.Instance);

        var json = await client.GetLinesJsonAsync("2026_11_22/abc");

        json.Should().Be(Fixture);
        api.Requests.Should().Equal(("https://api.mathpix.com/v3/pdf/2026_11_22%2Fabc.lines.json", true));
    }

    [Fact]
    public async Task The_client_throws_when_mathpix_doesnt_answer_200()
    {
        var api = new LinesApi(HttpStatusCode.NotFound, "{}");
        var client = new MathpixClient(new HttpClient(api), Options.Create(new MathpixOptions { AppKey = "k" }), NullLogger<MathpixClient>.Instance);

        await client.Invoking(c => c.GetLinesJsonAsync("x")).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void Percentiles_interpolate_as_percentile_cont()
    {
        double[] values = [100, 200, 300, 400, 500];
        Lilia.Api.Features.BulkImport.Services.BulkImportStats.Percentile(values, 0.5).Should().Be(300);
        Lilia.Api.Features.BulkImport.Services.BulkImportStats.Percentile(values, 0.9).Should().Be(460);
        Lilia.Api.Features.BulkImport.Services.BulkImportStats.Percentile([7], 0.9).Should().Be(7);
        Lilia.Api.Features.BulkImport.Services.BulkImportStats.Percentile([1, 2], 0.5).Should().Be(1.5);
    }

    [Fact]
    public void A_compile_error_keeps_its_first_line()
    {
        Lilia.Api.Features.BulkImport.Services.BulkImportProcessor.FirstLine("\n\n  ! Missing $ inserted.\nmore").Should().Be("! Missing $ inserted.");
        Lilia.Api.Features.BulkImport.Services.BulkImportProcessor.FirstLine(null).Should().Be("The compile failed.");
        Lilia.Api.Features.BulkImport.Services.BulkImportProcessor.FirstLine(new string('x', 900)).Should().HaveLength(500);
    }
}
