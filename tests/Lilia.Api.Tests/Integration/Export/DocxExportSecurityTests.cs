using System.Net;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// What a document is not allowed to make the server do.
///
/// <para>A figure block carries a <c>src</c> string written by whoever wrote
/// the document. Until 2026-09-09 the exporter issued an HTTP GET to it —
/// which made "export this document" a request the server performed on the
/// author's behalf, from inside the network. That is server-side request
/// forgery with a pleasant user interface: cloud metadata endpoints, internal
/// admin ports and the database's own host were all one figure block
/// away.</para>
///
/// <para>The rule is now simple enough to test: <b>no URL is ever fetched.</b>
/// An image exports if it is embedded in the block, or if it is an asset row
/// belonging to this document. Everything else is skipped, and the figure
/// keeps its caption.</para>
/// </summary>
[Collection("Integration")]
public class DocxExportSecurityTests : IntegrationTestBase
{
    private readonly string _userId = $"docx-s-{Guid.NewGuid():N}"[..28];

    public DocxExportSecurityTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private async Task<(HttpStatusCode Status, string Text, int ImageParts)> ExportFigureAsync(
        string contentJson)
    {
        var doc = await SeedDocumentAsync(_userId, "Figure security");
        await SeedBlockAsync(doc.Id, "figure", contentJson, 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"SENTINEL AFTER."}""", 1);

        var response = await CreateClientAs(_userId).GetAsync($"/api/documents/{doc.Id}/export/docx");
        if (response.StatusCode != HttpStatusCode.OK) return (response.StatusCode, "", 0);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);
        return (response.StatusCode,
                Regex.Replace(word.MainDocumentPart!.Document.OuterXml, "<[^>]+>", ""),
                word.MainDocumentPart.ImageParts.Count());
    }

    // ── the addresses that made this urgent ──────────────────────────────

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/",
                "cloud instance metadata — credentials")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/", "GCP metadata")]
    [InlineData("http://127.0.0.1:5432/", "the database, from inside")]
    [InlineData("http://localhost:5001/api/documents", "the API talking to itself")]
    [InlineData("http://[::1]:8025/", "the mail catcher, loopback-only by design")]
    [InlineData("http://10.0.0.1/admin", "private range")]
    [InlineData("http://192.168.1.1/", "private range")]
    [InlineData("file:///etc/passwd", "not even HTTP")]
    [InlineData("http://example.com/tracker.png", "an ordinary external URL — still not fetched")]
    public async Task ADocumentCannotMakeTheServerFetchAnAddress(string src, string why)
    {
        var (status, text, images) = await ExportFigureAsync(
            $$"""{"src":"{{src}}","caption":"A caption that must survive."}""");

        status.Should().Be(HttpStatusCode.OK, "one bad figure must not cost the document");
        images.Should().Be(0, $"nothing may be fetched from {why}");
        text.Should().Contain("A caption that must survive.");
        text.Should().Contain("SENTINEL AFTER.");
    }

    [Fact]
    public async Task AnAssetUrlBelongingToAnotherDocumentIsNotResolved()
    {
        // The lookup is scoped to the document being exported, so quoting
        // another document's asset URL gets you nothing — even though that URL
        // is perfectly valid and the asset really exists.
        var otherDoc = await SeedDocumentAsync(_userId, "Someone else's document");

        var (status, text, images) = await ExportFigureAsync(
            $$"""{"src":"https://assets.example/lilia/{{otherDoc.Id}}/figure.png","caption":"Borrowed."}""");

        status.Should().Be(HttpStatusCode.OK);
        images.Should().Be(0);
        text.Should().Contain("Borrowed.");
    }

    // ── what still works ─────────────────────────────────────────────────

    [Fact]
    public async Task AnImageEmbeddedInTheBlockStillExports()
    {
        const string onePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        var (status, text, images) = await ExportFigureAsync(
            "{\"caption\":\"An embedded figure.\",\"image\":{\"data\":\""
            + onePixelPng + "\",\"mimeType\":\"image/png\"}}");

        status.Should().Be(HttpStatusCode.OK);
        images.Should().Be(1, "embedding is the supported path and must keep working");
        text.Should().Contain("An embedded figure.");
    }

    [Fact]
    public async Task ADataUriInSrcStillExports()
    {
        const string dataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        var (status, _, images) = await ExportFigureAsync(
            "{\"src\":\"" + dataUri + "\",\"caption\":\"Inline data URI.\"}");

        status.Should().Be(HttpStatusCode.OK);
        images.Should().Be(1, "a data: URI is embedded content, not a network address");
    }

    [Fact]
    public async Task MalformedImageDataIsSkippedRatherThanFatal()
    {
        var (status, text, images) = await ExportFigureAsync(
            """{"caption":"Corrupt.","image":{"data":"@@@not-base64@@@","mimeType":"image/png"}}""");

        status.Should().Be(HttpStatusCode.OK);
        images.Should().Be(0);
        text.Should().Contain("SENTINEL AFTER.");
    }

    [Fact]
    public async Task AnEmptySrcIsHarmless()
    {
        var (status, text, images) = await ExportFigureAsync(
            """{"src":"","caption":"No image yet."}""");

        status.Should().Be(HttpStatusCode.OK);
        images.Should().Be(0);
        text.Should().Contain("No image yet.");
    }

    [Fact]
    public async Task ManyHostileFiguresDoNotSlowTheExportDown()
    {
        // If any of these were still being fetched, the timeouts alone would
        // blow this budget — ten unroutable addresses at a 10s timeout each.
        var doc = await SeedDocumentAsync(_userId, "Ten bad figures");
        for (var i = 0; i < 10; i++)
        {
            await SeedBlockAsync(doc.Id, "figure",
                $$"""{"src":"http://10.255.255.{{i}}/slow.png","caption":"Figure {{i}}."}""", i);
        }

        var started = DateTime.UtcNow;
        var response = await CreateClientAs(_userId).GetAsync($"/api/documents/{doc.Id}/export/docx");
        var elapsed = DateTime.UtcNow - started;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            "nothing is being dialled, so this should be near-instant");
    }
}
