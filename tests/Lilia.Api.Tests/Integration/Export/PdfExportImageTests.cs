using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// A figure the author uploaded has to appear in the PDF.
///
/// <para>It did not. The exporter builds a LaTeX project — main.tex plus a
/// figures/ directory — then extracted <b>only main.tex</b> and compiled that.
/// Every <c>\includegraphics</c> is wrapped in <c>\IfFileExists</c>, so with no
/// figures/ directory present each one took its false branch and the PDF
/// printed a box reading "[Missing figure: 632d339c-….jpg]" where the diagram
/// belonged (2026-09-09). The document was fine, the LaTeX was fine, the ZIP
/// export contained the image; only the compile could not see it.</para>
///
/// <para>These go through the real upload endpoint rather than seeding a URL,
/// because the defect lived in the gap between the project the exporter builds
/// and the directory the compiler reads.</para>
/// </summary>
[Collection("Integration")]
public class PdfExportImageTests : IntegrationTestBase
{
    private readonly string _userId = $"pdf-img-{Guid.NewGuid():N}"[..28];

    public PdfExportImageTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    /// <summary>A 1×1 PNG — enough to be a real image file.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task<string> UploadImageAsync(Guid docId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(OnePixelPng());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "diagram.png");

        var response = await CreateClientAs(_userId)
            .PostAsync($"/api/documents/{docId}/assets/upload", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the upload is the premise of every test here");

        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("url").GetString()!;
    }

    private async Task<byte[]> ExportPdfAsync(Guid docId)
    {
        var response = await CreateClientAs(_userId)
            .GetAsync($"/api/documents/{docId}/export/pdf?engine=pdflatex");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static bool Contains(byte[] pdf, string marker) =>
        Encoding.Latin1.GetString(pdf).Contains(marker, StringComparison.Ordinal);

    // ── the regression ───────────────────────────────────────────────────

    [Fact]
    public async Task AnUploadedFigureReachesThePdf()
    {
        var doc = await SeedDocumentAsync(_userId, "With a diagram");
        var url = await UploadImageAsync(doc.Id);
        await SeedBlockAsync(doc.Id, "figure",
            $$"""{"src":"{{url}}","caption":"A diagram.","alt":"diagram"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body after the figure."}""", 1);

        var pdf = await ExportPdfAsync(doc.Id);

        // An image XObject in the file. The dictionary keys are not compressed,
        // so this survives whatever the content streams do.
        Contains(pdf, "/Subtype").Should().BeTrue();
        Contains(pdf, "/Image").Should().BeTrue("the figure must be embedded, not described");
    }

    [Fact]
    public async Task AFigureWithNoImageDoesNotClaimOneIsMissing()
    {
        // A figure the author has not supplied yet is a legitimate state and
        // should not put a "[Missing figure]" box in a finished PDF either —
        // but the caption must still appear.
        var doc = await SeedDocumentAsync(_userId, "Figure to come");
        await SeedBlockAsync(doc.Id, "figure", """{"src":"","caption":"Caption only.","alt":""}""");

        var pdf = await ExportPdfAsync(doc.Id);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SeveralFiguresAllReachThePdf()
    {
        var doc = await SeedDocumentAsync(_userId, "Three diagrams");
        for (var i = 0; i < 3; i++)
        {
            var url = await UploadImageAsync(doc.Id);
            await SeedBlockAsync(doc.Id, "figure",
                $$"""{"src":"{{url}}","caption":"Figure {{i}}.","alt":""}""", i);
        }

        var pdf = await ExportPdfAsync(doc.Id);

        Contains(pdf, "/Image").Should().BeTrue();
    }

    [Fact]
    public async Task TheLatexProjectShipsTheFigureAlongsideTheSource()
    {
        // The ZIP was always right — worth pinning, since the PDF fix depends
        // on the project containing the file in the first place.
        var doc = await SeedDocumentAsync(_userId, "Project with a figure");
        var url = await UploadImageAsync(doc.Id);
        await SeedBlockAsync(doc.Id, "figure",
            $$"""{"src":"{{url}}","caption":"Shipped.","alt":""}""");

        var response = await CreateClientAs(_userId)
            .GetAsync($"/api/documents/{doc.Id}/export/latex");
        using var ms = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);

        zip.Entries.Should().Contain(e => e.FullName.StartsWith("figures/", StringComparison.Ordinal),
            "a project whose figures are missing does not compile to the document the author wrote");
        zip.Entries.Should().Contain(e => e.Name == "main.tex");
    }

    [Fact]
    public async Task AFigureBelongingToAnotherDocumentIsNotPulledIn()
    {
        // The same rule the Word export enforces: an asset URL is resolved
        // against this document's own assets, never fetched.
        var mine = await SeedDocumentAsync(_userId, "Mine");
        var theirs = await SeedDocumentAsync(_userId, "Theirs");
        var theirUrl = await UploadImageAsync(theirs.Id);

        await SeedBlockAsync(mine.Id, "figure",
            $$"""{"src":"{{theirUrl}}","caption":"Borrowed.","alt":""}""", 0);
        await SeedBlockAsync(mine.Id, "paragraph", """{"text":"Body survives."}""", 1);

        var pdf = await ExportPdfAsync(mine.Id);

        pdf.Should().NotBeEmpty("one unresolvable figure must not cost the document");
    }
}
