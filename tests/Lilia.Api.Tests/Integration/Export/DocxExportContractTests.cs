using System.Net;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// The export endpoint as a product: what a paying caller is entitled to
/// assume, independent of what any particular document contains.
///
/// <para>Everything here is about the contract rather than the content — the
/// status code, the media type, the filename, who is allowed to call it, and
/// whether two identical calls agree. A document exporter that is occasionally
/// wrong about its own MIME type is not something anyone can build on.</para>
/// </summary>
[Collection("Integration")]
public class DocxExportContractTests : IntegrationTestBase
{
    private const string DocxMediaType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly string _userId = $"docx-c-{Guid.NewGuid():N}"[..28];

    public DocxExportContractTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private Task<HttpResponseMessage> GetAsync(Guid docId, string? asUser = null) =>
        CreateClientAs(asUser ?? _userId).GetAsync($"/api/documents/{docId}/export/docx");

    private async Task<Guid> ASmallDocumentAsync(string? title = null)
    {
        var doc = await SeedDocumentAsync(_userId, title ?? "Contract test");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""");
        return doc.Id;
    }

    // ── status and media type ────────────────────────────────────────────

    [Fact]
    public async Task ReturnsTwoHundredAndTheWordMediaType()
    {
        var response = await GetAsync(await ASmallDocumentAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(DocxMediaType,
            "callers dispatch on this; a generic octet-stream would make them guess");
    }

    [Fact]
    public async Task TheBodyIsAValidOpenXmlPackage()
    {
        var response = await GetAsync(await ASmallDocumentAsync());
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using var ms = new MemoryStream(bytes);
        var open = () => WordprocessingDocument.Open(ms, false);
        open.Should().NotThrow("the bytes must be openable by any OOXML reader, not just Word");
    }

    [Fact]
    public async Task ContentLengthMatchesTheBodyActuallySent()
    {
        var response = await GetAsync(await ASmallDocumentAsync());
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.Content.Headers.ContentLength.Should().Be(bytes.Length);
    }

    // ── the filename ─────────────────────────────────────────────────────

    [Fact]
    public async Task OffersTheDocumentAsADownloadNamedAfterItsTitle()
    {
        var response = await GetAsync(await ASmallDocumentAsync("Quarterly Report"));

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull();
        disposition!.DispositionType.Should().Be("attachment");
        (disposition.FileName ?? disposition.FileNameStar).Should().Contain("Quarterly");
        (disposition.FileName ?? disposition.FileNameStar).Should().EndWith(".docx");
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("report/with/slashes")]
    [InlineData("quotes \" and ; semicolons")]
    public async Task ATitleCannotSmuggleAnythingIntoTheFilename(string hostileTitle)
    {
        var doc = await SeedDocumentAsync(_userId, hostileTitle);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""");

        var response = await GetAsync(doc.Id);
        var name = response.Content.Headers.ContentDisposition?.FileName
                   ?? response.Content.Headers.ContentDisposition?.FileNameStar
                   ?? "";

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // A NUL byte is absent from this list deliberately: Postgres rejects
        // 0x00 in a text column, so a title containing one cannot be stored and
        // never reaches the exporter.
        name.Trim('"').Should().NotContain("/").And.NotContain("\\");
        name.Trim('"').Should().EndWith(".docx");
    }

    [Fact]
    public async Task AnUntitledDocumentStillGetsAUsableFilename()
    {
        var doc = await SeedDocumentAsync(_userId, "");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""");

        var response = await GetAsync(doc.Id);
        var name = response.Content.Headers.ContentDisposition?.FileName
                   ?? response.Content.Headers.ContentDisposition?.FileNameStar ?? "";

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        name.Should().Contain(".docx");
    }

    // ── who may call it ──────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownDocumentIsNotFound()
    {
        var response = await GetAsync(Guid.NewGuid());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe(DocxMediaType,
            "an error must not arrive dressed as a Word file");
    }

    [Fact]
    public async Task AnotherUsersDocumentIsRefused()
    {
        var stranger = $"docx-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        var theirDoc = await SeedDocumentAsync(stranger, "Private research");
        await SeedBlockAsync(theirDoc.Id, "paragraph", """{"text":"Unpublished."}""");

        var response = await GetAsync(theirDoc.Id);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Unpublished.", "no content may leak through the error path");
    }

    [Fact]
    public async Task AnAnonymousCallerIsRefused()
    {
        var docId = await ASmallDocumentAsync();

        var response = await CreateAnonymousClient()
            .GetAsync($"/api/documents/{docId}/export/docx");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AMalformedIdIsRejectedCleanly()
    {
        var response = await CreateClientAs(_userId)
            .GetAsync("/api/documents/not-a-guid/export/docx");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    // ── consistency ──────────────────────────────────────────────────────

    [Fact]
    public async Task TwoIdenticalCallsProduceTheSameDocument()
    {
        // Not byte-equality — an OOXML package carries timestamps — but the
        // rendered text must not wander between calls.
        var docId = await ASmallDocumentAsync("Stable");
        await SeedBlockAsync(docId, "heading", """{"text":"Section One","level":1}""", 1);

        string TextOf(byte[] b)
        {
            using var ms = new MemoryStream(b);
            using var w = WordprocessingDocument.Open(ms, false);
            return System.Text.RegularExpressions.Regex.Replace(
                w.MainDocumentPart!.Document.OuterXml, "<[^>]+>", "");
        }

        var first = TextOf(await (await GetAsync(docId)).Content.ReadAsByteArrayAsync());
        var second = TextOf(await (await GetAsync(docId)).Content.ReadAsByteArrayAsync());

        second.Should().Be(first);
    }

    [Fact]
    public async Task AnEmptyDocumentIsAValidExportRatherThanAnError()
    {
        // A document someone just created is a legitimate thing to export.
        var doc = await SeedDocumentAsync(_userId, "Nothing written yet");

        var response = await GetAsync(doc.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ALargeDocumentExportsWithinAReasonableTime()
    {
        var doc = await SeedDocumentAsync(_userId, "Long report");
        for (var i = 0; i < 200; i++)
        {
            await SeedBlockAsync(doc.Id, "paragraph",
                $$"""{"text":"Paragraph {{i}} of a long report with enough words to be realistic."}""", i);
        }

        var started = DateTime.UtcNow;
        var response = await GetAsync(doc.Id);
        var elapsed = DateTime.UtcNow - started;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "a 200-block report is ordinary; this is a smoke alarm for anything quadratic");
    }
}
