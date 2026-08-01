using System.Net;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Mathpix credentials and the availability check.
///
/// <para>Two separate failures live here. The first refused a valid
/// credential: keys issued by the Mathpix console today come on their own, and
/// the client demanded a paired <c>app_id</c> that no longer exists — so PDF
/// import was disabled before a request was ever made.</para>
///
/// <para>The second is the more dangerous one. The check probed
/// <c>GET /v3/pdf</c>, a route that accepts POST only and answers 404 to
/// everyone. Measured against the live API: <c>/v3/pdf</c> returns 404 with a
/// good key, a bad key, and no key at all, while <c>/v3/pdf-results</c> returns
/// 200 and 401 respectively. The check reported "available" for credentials
/// Mathpix would reject on the next call — a health check that could not fail
/// was worse than no health check, because it was believed.</para>
/// </summary>
public class MathpixCredentialTests
{
    /// <summary>Captures the outgoing request and replays a canned status.</summary>
    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}"),
                RequestMessage = request,
            });
        }
    }

    private static (MathpixClient Client, CapturingHandler Handler) Sut(
        string appKey = "test-key",
        string appId = "",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(status);
        var http = new HttpClient(handler);
        var options = Options.Create(new MathpixOptions { AppId = appId, AppKey = appKey });

        return (new MathpixClient(http, options, NullLogger<MathpixClient>.Instance), handler);
    }

    // ── The credential ────────────────────────────────────────────────

    [Fact]
    public void A_key_on_its_own_is_a_complete_credential()
    {
        var (_, handler) = Sut(appKey: "only-a-key");

        // Constructing at all is the assertion: the old code added an empty
        // app_id header here, and the availability guard then refused to run.
        handler.Should().NotBeNull();
    }

    [Fact]
    public async Task No_empty_app_id_header_is_sent()
    {
        // An empty header is worse than an absent one — it is a value Mathpix
        // may reject, and it makes a working key look broken.
        var (client, handler) = Sut(appId: "");

        await client.IsAvailableAsync();

        handler.LastRequest!.Headers.Contains("app_id").Should().BeFalse();
        handler.LastRequest.Headers.GetValues("app_key").Should().ContainSingle();
    }

    [Fact]
    public async Task A_paired_app_id_is_still_sent_when_configured()
    {
        // The legacy form has to keep working; this is a widening, not a swap.
        var (client, handler) = Sut(appId: "legacy_app");

        await client.IsAvailableAsync();

        handler.LastRequest!.Headers.GetValues("app_id").Should().ContainSingle("legacy_app");
    }

    [Fact]
    public async Task Without_a_key_nothing_is_attempted()
    {
        var (client, handler) = Sut(appKey: "");

        (await client.IsAvailableAsync()).Should().BeFalse();
        handler.LastRequest.Should().BeNull("there is nothing to ask with");
    }

    // ── The availability check ────────────────────────────────────────

    [Fact]
    public async Task The_probe_asks_a_route_that_actually_authenticates()
    {
        var (client, handler) = Sut();

        await client.IsAvailableAsync();

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v3/pdf-results");
    }

    [Fact]
    public async Task A_good_key_reports_available()
    {
        var (client, _) = Sut(status: HttpStatusCode.OK);

        (await client.IsAvailableAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Rejected_credentials_report_unavailable()
    {
        var (client, _) = Sut(status: HttpStatusCode.Unauthorized);

        (await client.IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_404_is_not_read_as_healthy()
    {
        // The exact regression. The old check returned true for anything that
        // was not a 401, so a 404 — which is what the wrong route always gave —
        // meant "Mathpix is fine". Now only a success counts as proof.
        var (client, _) = Sut(status: HttpStatusCode.NotFound);

        (await client.IsAvailableAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Anything_short_of_a_success_is_reported_as_unavailable(HttpStatusCode status)
    {
        // None of these prove the credentials are bad — but none prove they are
        // good either, and "I could not tell" belongs on the unavailable side.
        var (client, _) = Sut(status: status);

        (await client.IsAvailableAsync()).Should().BeFalse();
    }
}
