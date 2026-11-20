using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// Security hotfix (20 Nov 2026): MathpixClient.GetImageAsync fetched any address found in Mathpix's Markdown, through the
/// client that sends the app_key header on every request. A scanned page whose text came out as an image link to another
/// host would have sent the key there, or reached an internal address from the server. Figures now come from Mathpix's CDN
/// only, through a client with no key and no redirects.
/// </summary>
public class MathpixImageFetchSecurityTests
{
    [Theory]
    [InlineData("https://cdn.mathpix.com/cropped/2026_11_20_abc.jpg?height=200&width=300")]
    [InlineData("https://CDN.MATHPIX.COM/cropped/x.png")]
    public void A_figure_on_Mathpix_CDN_is_allowed(string url) => MathpixClient.IsMathpixImage(url).Should().BeTrue();

    [Theory]
    [InlineData("http://cdn.mathpix.com/cropped/x.png")]                 // not https
    [InlineData("https://cdn.mathpix.com.evil.example/x.png")]           // look-alike host
    [InlineData("https://evil.example/x.png")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]            // cloud metadata
    [InlineData("https://127.0.0.1:5001/api/documents")]                 // the API itself
    [InlineData("https://cdn.mathpix.com:8443/x.png")]                   // another port
    [InlineData("https://user:pass@cdn.mathpix.com/x.png")]              // user info
    [InlineData("file:///etc/passwd")]
    [InlineData("/v3/pdf/abc.md")]                                      // relative: would hit api.mathpix.com with the key
    [InlineData("")]
    public void Any_other_address_is_refused(string url) => MathpixClient.IsMathpixImage(url).Should().BeFalse();

    [Fact]
    public async Task A_refused_address_is_never_requested()
    {
        var handler = new RecordingHandler();
        var client = new MathpixClient(new HttpClient(handler),
            Options.Create(new MathpixOptions { AppKey = "secret-key", BaseUrl = "https://api.mathpix.com" }),
            NullLogger<MathpixClient>.Instance);

        var act = () => client.GetImageAsync("https://evil.example/steal.png");

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().Be(0, "the keyed client must never be used for a figure");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
