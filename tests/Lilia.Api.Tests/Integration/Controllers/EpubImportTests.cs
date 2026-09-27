using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// POST /api/lilia/imports/epub — an ePub goes through the import job and the
/// import review, like .tex and .docx (design decision, 27 Sep 2026:
/// lilia-docs/design-handoffs/2026-09-27-templates-epub-slash/REPLY-from-olivia.md §3).
///
/// The book under test is made by the server's own ePub exporter from two
/// chapters, the way the e2e suite makes one, so these tests round-trip
/// the real format rather than a fixture that could drift from it.
/// </summary>
[Collection("Integration")]
public class EpubImportTests : IntegrationTestBase
{
    private const string UserId = "test_user_epub_import";
    private readonly HttpClient _client;

    private static readonly (string Heading, string Body)[] Chapters =
    [
        ("Chapter One: The Harbour", "The tide came in slowly over the grey stones of the harbour wall."),
        ("Chapter Two: The Lighthouse", "Nobody had climbed the lighthouse stairs since the keeper left in spring."),
    ];

    public EpubImportTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _client = CreateClientAs(UserId);
    }

    /// <summary>An .epub of <see cref="Chapters"/>, from POST /api/lilia/epub/export.</summary>
    private async Task<byte[]> ExportedBookAsync(string title)
    {
        var blocks = new List<object>();
        var order = 0;
        foreach (var (heading, body) in Chapters)
        {
            blocks.Add(new { type = "heading", content = new { text = heading, level = 1 }, sortOrder = order++ });
            blocks.Add(new { type = "paragraph", content = new { text = body }, sortOrder = order++ });
        }
        var res = await _client.PostAsJsonAsync("/api/lilia/epub/export", new { blocks, options = new { title } });
        res.StatusCode.Should().Be(HttpStatusCode.OK, "the server builds an epub from blocks");
        return await res.Content.ReadAsByteArrayAsync();
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string fileName, bool autoFinalize = false, HttpClient? client = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/epub+zip");
        form.Add(file, "file", fileName);
        var qs = autoFinalize ? "?autoFinalize=true" : "";
        return await (client ?? _client).PostAsync($"/api/lilia/imports/epub{qs}", form);
    }

    /// <summary>
    /// A hand-made book the exporter would never write: one chapter styled
    /// inline, and a second the spine names but the zip does not contain.
    /// </summary>
    private static byte[] RoughBook()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string name, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Put("mimetype", "application/epub+zip");
            Put("META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """);
            Put("OEBPS/content.opf", """
                <?xml version="1.0"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>A Rough Book</dc:title></metadata>
                  <manifest>
                    <item id="c1" href="c1.xhtml" media-type="application/xhtml+xml"/>
                    <item id="c2" href="c2.xhtml" media-type="application/xhtml+xml"/>
                  </manifest>
                  <spine><itemref idref="c1"/><itemref idref="c2"/></spine>
                </package>
                """);
            Put("OEBPS/c1.xhtml", """
                <?xml version="1.0"?>
                <html xmlns="http://www.w3.org/1999/xhtml"><body>
                  <h1>Only Chapter</h1>
                  <p style="color:red">Styled one.</p>
                  <p style="font-weight:bold">Styled two.</p>
                </body></html>
                """);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Upload_Epub_CreatesJobAndReviewSession_AndFinalizeMakesTheBook()
    {
        await SeedUserAsync(UserId);
        var book = await ExportedBookAsync("The Harbour Book");

        var res = await UploadAsync(book, "harbour.epub");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var ids = await res.Content.ReadFromJsonAsync<LatexImportUploadResponseDto>();
        ids!.SessionId.Should().NotBe(Guid.Empty);
        ids.JobId.Should().NotBe(Guid.Empty);

        // The job: an inbound ePub import, done parsing, no document yet.
        await using (var db = CreateDbContext())
        {
            var job = await db.Jobs.SingleAsync(j => j.Id == ids.JobId);
            job.UserId.Should().Be(UserId);
            job.JobType.Should().Be(JobTypes.Import);
            job.SourceFormat.Should().Be("epub");
            job.SourceFileName.Should().Be("harbour.epub");
            job.Status.Should().Be(JobStatus.Completed);
            job.DocumentId.Should().BeNull("the review has not been accepted");

            var session = await db.ImportReviewSessions.SingleAsync(s => s.Id == ids.SessionId);
            session.JobId.Should().Be(ids.JobId);
            session.OwnerId.Should().Be(UserId);
            session.SourceFormat.Should().Be("epub");
            session.Status.Should().Be("in_progress");
            session.DocumentTitle.Should().Be("The Harbour Book", "the book's own title, from its package");
            session.DocumentId.Should().BeNull();

            var staged = await db.ImportBlockReviews.Where(b => b.SessionId == ids.SessionId).ToListAsync();
            var stagedText = string.Join("\n", staged.Select(b => b.OriginalContent.RootElement.GetRawText()));
            foreach (var (heading, body) in Chapters)
            {
                stagedText.Should().Contain(heading);
                stagedText.Should().Contain(body);
            }
            (await db.Documents.CountAsync(d => d.OwnerId == UserId)).Should().Be(0, "nothing becomes a document before review");
        }

        // The job points at its review, as it does for DOCX.
        var jobRes = await _client.GetAsync($"/api/lilia/jobs/{ids.JobId}");
        jobRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var jobJson = JsonDocument.Parse(await jobRes.Content.ReadAsStringAsync()))
            jobJson.RootElement.GetProperty("reviewSessionId").GetGuid().Should().Be(ids.SessionId);

        // Accept the review: the book becomes a document.
        var fin = await _client.PostAsJsonAsync(
            $"/api/lilia/import-review/sessions/{ids.SessionId}/finalize", new FinalizeSessionDto(Force: true));
        fin.StatusCode.Should().Be(HttpStatusCode.OK, await fin.Content.ReadAsStringAsync());
        var result = await fin.Content.ReadFromJsonAsync<FinalizeResultDto>();
        var docId = result!.Document.Id;

        await using (var db = CreateDbContext())
        {
            var doc = await db.Documents.SingleAsync(d => d.Id == docId);
            doc.OwnerId.Should().Be(UserId);
            doc.Title.Should().Be("The Harbour Book");

            var blocks = await db.Blocks.Where(b => b.DocumentId == docId).OrderBy(b => b.SortOrder).ToListAsync();
            var text = string.Join("\n", blocks.Select(b => b.Content.RootElement.GetRawText()));
            foreach (var (heading, body) in Chapters)
            {
                text.Should().Contain(heading);
                text.Should().Contain(body);
            }
            blocks.Should().Contain(b => b.Type == "heading" && b.Content.RootElement.GetRawText().Contains(Chapters[0].Heading));
            blocks.Should().Contain(b => b.Type == "paragraph" && b.Content.RootElement.GetRawText().Contains(Chapters[1].Body));
            // Reading order survives: chapter one's heading before chapter two's.
            var first = blocks.FindIndex(b => b.Content.RootElement.GetRawText().Contains(Chapters[0].Heading));
            var second = blocks.FindIndex(b => b.Content.RootElement.GetRawText().Contains(Chapters[1].Heading));
            first.Should().BeLessThan(second);

            (await db.Jobs.SingleAsync(j => j.Id == ids.JobId)).DocumentId.Should().Be(docId, "finalize links the job to its document");
            (await db.ImportReviewSessions.SingleAsync(s => s.Id == ids.SessionId)).Status.Should().Be("imported");
        }
    }

    [Fact]
    public async Task Upload_Epub_WithAutoFinalize_MakesTheDocumentAtOnce()
    {
        await SeedUserAsync(UserId);
        var res = await UploadAsync(await ExportedBookAsync("Straight In"), "straight.epub", autoFinalize: true);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var ids = await res.Content.ReadFromJsonAsync<LatexImportUploadResponseDto>();

        await using var db = CreateDbContext();
        var job = await db.Jobs.SingleAsync(j => j.Id == ids!.JobId);
        job.Status.Should().Be(JobStatus.Completed);
        job.DocumentId.Should().NotBeNull("the page follows job.documentId to the editor");
        var text = string.Join("\n", (await db.Blocks.Where(b => b.DocumentId == job.DocumentId).ToListAsync())
            .Select(b => b.Content.RootElement.GetRawText()));
        text.Should().Contain(Chapters[1].Body);
    }

    [Fact]
    public async Task Upload_RoughEpub_ShowsWhatCleanUpDroppedAsDiagnostics()
    {
        await SeedUserAsync(UserId);
        var res = await UploadAsync(RoughBook(), "rough.epub");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var ids = await res.Content.ReadFromJsonAsync<LatexImportUploadResponseDto>();

        var diag = await _client.GetAsync($"/api/lilia/import-review/sessions/{ids!.SessionId}/diagnostics");
        diag.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await diag.Content.ReadAsStringAsync();
        body.Should().Contain("EPUB.MISSING_CHAPTER", "the spine names c2.xhtml and the zip has none");
        body.Should().Contain("EPUB.INLINE_STYLE_DROPPED");
        body.Should().Contain("2 <p> elements had inline styles", "one row per tag, counted, not one per element");

        await using var db = CreateDbContext();
        var staged = string.Join("\n", (await db.ImportBlockReviews.Where(b => b.SessionId == ids.SessionId).ToListAsync())
            .Select(b => b.OriginalContent.RootElement.GetRawText()));
        staged.Should().Contain("Styled one.").And.Contain("Styled two.", "styling is dropped, the text is kept");
    }

    [Theory]
    [InlineData("notes.txt", "just some text")]
    [InlineData("disguised.epub", "just some text, named like a book")]
    public async Task Upload_NotAnEpub_Returns400_AndWritesNothing(string fileName, string content)
    {
        await SeedUserAsync(UserId);
        var res = await UploadAsync(Encoding.UTF8.GetBytes(content), fileName);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        (await db.Jobs.CountAsync(j => j.UserId == UserId)).Should().Be(0);
        (await db.ImportReviewSessions.CountAsync(s => s.OwnerId == UserId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_ZipThatIsNotABook_Returns400()
    {
        await SeedUserAsync(UserId);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("readme.txt").Open());
            w.Write("not a book");
        }

        var res = await UploadAsync(ms.ToArray(), "archive.epub");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("isn't an ePub");

        await using var db = CreateDbContext();
        (await db.Jobs.CountAsync(j => j.UserId == UserId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_Anonymous_Returns401()
    {
        using var anon = CreateAnonymousClient();
        var res = await UploadAsync(RoughBook(), "rough.epub", client: anon);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
