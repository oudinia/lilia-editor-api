using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Features.Import;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// PDF and scan import, 20 Nov 2026: the launch bot's handwritten maths notes were refused by the connector's
/// <c>import_document</c> ("Lilia imports .tex, .zip, .docx, .md and .lml"), though the editor imported PDFs. Now a .pdf,
/// .jpg or .png goes in as base64, is checked here (its bytes, its size, the plan's PDF pages) before anything reaches
/// Mathpix, then imports in the background: the reply is an importId, and <c>import_status</c> says working, then done
/// or failed. It is metered as the editor's upload is, through <c>EnsureCanImportPdfAsync</c>: a PDF by its pages, an
/// image as one page; past the plan's monthly pages it is refused with a message naming that limit, never a 500.
///
/// <para>Mathpix is <see cref="MathpixTexZipTests.FakeMathpix"/> behind the real client and import service: no file
/// leaves the machine.</para>
/// </summary>
public class McpScanImportTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public McpScanImportTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private static string NewId(string what) => $"scan_{what}_{Guid.NewGuid():N}"[..30];

    private async Task GiveProAsync(string userId)
    {
        await using var db = CreateDbContext();
        var pro = await db.Plans.SingleAsync(p => p.Slug == "pro");
        db.UserPlans.Add(new Lilia.Core.Entities.UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>A host whose Mathpix is the fake, for PDFs and scans alike.</summary>
    private WebApplicationFactory<Program> HostWith(MathpixTexZipTests.FakeMathpix api)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(IPdfParser) || d.ServiceType == typeof(IScanImageParser)).ToList())
                services.Remove(d);
            services.AddScoped<IPdfParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
            services.AddScoped<IScanImageParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
        }));
        _factories.Add(factory);
        return factory;
    }

    /// <summary>The author on that host: through a connected app (only /api/mcp), or signed in to Lilia itself.</summary>
    private static HttpClient ClientOf(WebApplicationFactory<Program> factory, string userId, bool asApp = true)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", userId);
        if (!asApp) return client;
        client.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        client.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");
        return client;
    }

    private HttpClient AppWith(MathpixTexZipTests.FakeMathpix api, string userId) => ClientOf(HostWith(api), userId);

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var text = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    /// <summary>import_status until the background import is no longer working (at most 60 s).</summary>
    private static async Task<JsonElement> SettledAsync(HttpClient client, string importId)
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var status = await CallAsync(client, "import_status", new { importId });
            if (status.GetProperty("status").GetString() != "working" || DateTime.UtcNow > until) return status;
            await Task.Delay(500);
        }
    }

    private async Task<(int Pages, List<string> Formats)> PagesChargedAsync(string userId)
    {
        await using var db = CreateDbContext();
        var ops = await db.Operations.AsNoTracking().Where(o => o.UserId == userId && o.Kind == "import.pdf").ToListAsync();
        return (ops.Sum(o => o.Units ?? 0), ops.Select(o => o.Format).ToList());
    }

    private static readonly string FakePdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 fake"));
    private static readonly string Png = Convert.ToBase64String(ScanImageImportTests.Png);

    [Fact]
    public async Task A_PDF_sent_to_import_document_answers_with_an_importId_and_becomes_a_document_charged_its_pages()
    {
        var user = NewId("pdf");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var app = AppWith(api, user);

        var started = await CallAsync(app, "import_document", new { filename = "handwritten-notes.pdf", content = $"data:application/pdf;base64,{FakePdf}" });
        started.TryGetProperty("error", out _).Should().BeFalse(started.ToString());
        started.GetProperty("status").GetString().Should().Be("working");
        started.GetProperty("next").GetString().Should().Contain("import_status");

        var done = await SettledAsync(app, started.GetProperty("importId").GetString()!);
        done.GetProperty("status").GetString().Should().Be("done", done.ToString());
        var id = done.GetProperty("documentId").GetGuid();
        done.GetProperty("url").GetString().Should().EndWith($"/d/{id}");

        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == id)).OwnerId.Should().Be(user);
        (await db.Blocks.AsNoTracking().CountAsync(b => b.DocumentId == id && b.Type == "heading")).Should().BeGreaterThan(0);
        // The 2 pages Mathpix reported, against the plan's PDF pages, as the editor's upload counts them.
        (await PagesChargedAsync(user)).Pages.Should().Be(2);
        api.UploadOptions!.RootElement.GetProperty("metadata").GetProperty("improve_mathpix").GetBoolean().Should().BeFalse();
        // A clean reading: nothing to check against the scan, and the tex.zip's file summary ("Found 1 image file(s)")
        // is not a warning for the author.
        done.GetProperty("warnings").GetArrayLength().Should().Be(0, done.ToString());
        done.GetProperty("warningsTotal").GetInt32().Should().Be(0);
        done.GetProperty("next").GetString().Should().NotContain("warning");
    }

    /// <summary>
    /// The launch bot's retest of API #672 (20 Nov 2026): a scan whose formula held a symbol Mathpix couldn't read came
    /// through the connector with a □ in the document and no word about it, since the connector's import has no review.
    /// import_status now gives the review's warnings and tells the assistant to have the author check them.
    /// </summary>
    [Theory]
    [InlineData("texzip")]
    [InlineData("markdown")]
    public async Task A_scan_with_an_unreadable_symbol_reports_the_boxed_symbol_in_import_status(string route)
    {
        var user = NewId("box" + route[..2]);
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var api = route == "texzip"
            ? new MathpixTexZipTests.FakeMathpix
            {
                TexZipDownload = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ScanFiguresAndSymbolsTests.HandwrittenTexZip()) },
            }
            : new MathpixTexZipTests.FakeMathpix { TexZipStatuses = new(["error"]), MarkdownBody = ScanFiguresAndSymbolsTests.HandwrittenMarkdown };
        var app = AppWith(api, user);

        var started = await CallAsync(app, "import_document", new { filename = "handwritten.png", content = Png });
        started.TryGetProperty("warnings", out _).Should().BeFalse("nothing is known before the import is done");
        var done = await SettledAsync(app, started.GetProperty("importId").GetString()!);

        done.GetProperty("status").GetString().Should().Be("done", done.ToString());
        var warnings = done.GetProperty("warnings").EnumerateArray().ToList();
        var boxed = warnings.Where(w => w.GetProperty("code").GetString() == "UnreadableSymbolReplaced").ToList();
        boxed.Should().NotBeEmpty(done.ToString());
        boxed.Should().OnlyContain(w => w.GetProperty("message").GetString()!.Contains("replaced with a box (□)")
            && w.GetProperty("message").GetString()!.Contains("Check it against the scan"));
        var total = done.GetProperty("warningsTotal").GetInt32();
        total.Should().BeGreaterThanOrEqualTo(warnings.Count);
        warnings.Count.Should().Be(Math.Min(total, Lilia.Api.Mcp.ConnectorTransfers.ImportWarning.MaxShown));
        var next = done.GetProperty("next").GetString()!;
        next.Should().Contain($"{total} warning").And.Contain("Tell the author").And.Contain("against the original scan");

        // The same warnings as the review session's diagnostics.
        await using var db = CreateDbContext();
        var id = done.GetProperty("documentId").GetGuid();
        var sessionId = await db.ImportReviewSessions.AsNoTracking().Where(s => s.DocumentId == id).Select(s => s.Id).SingleAsync();
        (await db.ImportDiagnostics.AsNoTracking().CountAsync(d => d.SessionId == sessionId && !d.Dismissed && d.Code != "" && (d.Severity == "error" || d.Severity == "warning")))
            .Should().Be(total);
    }

    [Fact]
    public async Task A_scan_dropped_on_the_drop_page_reports_its_warnings_in_import_status()
    {
        var user = NewId("dropbox");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var host = HostWith(new MathpixTexZipTests.FakeMathpix { TexZipStatuses = new(["error"]), MarkdownBody = ScanFiguresAndSymbolsTests.HandwrittenMarkdown });
        var app = ClientOf(host, user);
        var author = ClientOf(host, user, asApp: false);

        var importId = (await CallAsync(app, "start_import", new { })).GetProperty("importId").GetString()!;
        var waiting = await CallAsync(app, "import_status", new { importId });
        (!waiting.TryGetProperty("warnings", out var none) || none.ValueKind == JsonValueKind.Null).Should().BeTrue(waiting.ToString());

        var post = await author.PostAsJsonAsync($"/api/connector/imports/{importId}", new { content = Png, format = "IMAGE", filename = "notes.png" });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        var done = await CallAsync(app, "import_status", new { importId });
        done.GetProperty("status").GetString().Should().Be("done");
        done.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("UnreadableSymbolReplaced");
        done.GetProperty("next").GetString().Should().Contain("Tell the author");
    }

    [Fact]
    public async Task A_PDF_whose_LaTeX_failed_is_still_a_document_from_the_Markdown_through_the_connector()
    {
        var user = NewId("md");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var app = AppWith(new MathpixTexZipTests.FakeMathpix { TexZipStatuses = new(["error"]) }, user);

        var started = await CallAsync(app, "import_document", new { filename = "notes.pdf", content = FakePdf });
        var done = await SettledAsync(app, started.GetProperty("importId").GetString()!);

        // Before, the Markdown fallback made only a review, and the connector's import answered "no document".
        done.GetProperty("status").GetString().Should().Be("done", done.ToString());
        await using var db = CreateDbContext();
        var id = done.GetProperty("documentId").GetGuid();
        (await db.Blocks.AsNoTracking().Where(b => b.DocumentId == id).Select(b => b.Content).ToListAsync())
            .Should().Contain(c => c.RootElement.GetRawText().Contains("read from the Markdown"));
    }

    // Free has 10 PDF pages a month since 20 Nov 2026 (the owner's pricing decision; PdfImportCreditsTests covers credits).
    [Fact]
    public async Task Free_past_its_10_pages_is_refused_a_PDF_or_an_image_with_the_limit_named_and_Mathpix_is_never_called()
    {
        var user = NewId("free");
        await SeedUserAsync(user);
        await using (var db = CreateDbContext())
        {
            db.Operations.Add(new Lilia.Core.Entities.Operation { Id = Guid.NewGuid(), UserId = user, Kind = "import.pdf", Points = 15, Units = 10, Format = "pdf", Source = "screens" });
            await db.SaveChangesAsync();
        }
        var api = new MathpixTexZipTests.FakeMathpix();
        var app = AppWith(api, user);

        foreach (var (file, content) in new[] { ("notes.pdf", FakePdf), ("notes.png", Png) })
        {
            var refused = await CallAsync(app, "import_document", new { filename = file, content });
            refused.GetProperty("category").GetString().Should().Be("plan-limit");
            refused.GetProperty("error").GetString().Should().Contain("This month's 10 PDF pages are used (10 a month on");
            refused.GetProperty("limit").GetProperty("resource").GetString().Should().Be("pdfPagesPerMonth");
            refused.TryGetProperty("importId", out _).Should().BeFalse();
        }

        api.Requests.Should().BeEmpty("a refused import costs nothing");
        (await PagesChargedAsync(user)).Pages.Should().Be(10);
    }

    [Fact]
    public async Task A_scanned_page_sent_as_a_png_becomes_a_document_charged_one_page()
    {
        var user = NewId("png");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var app = AppWith(api, user);

        var started = await CallAsync(app, "import_document", new { filename = "handwritten-notes-scan.png", content = Png });
        started.TryGetProperty("error", out _).Should().BeFalse(started.ToString());
        started.GetProperty("pages").GetInt32().Should().Be(1);
        var done = await SettledAsync(app, started.GetProperty("importId").GetString()!);
        done.GetProperty("status").GetString().Should().Be("done", done.ToString());

        await using var db = CreateDbContext();
        var id = done.GetProperty("documentId").GetGuid();
        var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == id).ToListAsync();
        // Read as a one-page PDF since 20 Nov 2026: its tex.zip, figure included.
        blocks.Should().Contain(b => b.Type == "equation");
        blocks.Should().Contain(b => b.Content.RootElement.GetRawText().Contains("read from the tex.zip"));
        blocks.Should().Contain(b => b.Type == "figure");
        api.Requests.First().Should().Be("POST api.mathpix.com/v3/pdf");
        api.UploadOptions!.RootElement.GetProperty("metadata").GetProperty("improve_mathpix").GetBoolean().Should().BeFalse();
        var charged = await PagesChargedAsync(user);
        charged.Pages.Should().Be(1);
        charged.Formats.Should().Equal("image");
    }

    [Fact]
    public async Task An_image_Mathpix_could_not_read_fails_in_words_and_its_page_is_still_counted()
    {
        var user = NewId("noread");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var app = AppWith(new MathpixTexZipTests.FakeMathpix { PdfStatus = "error" }, user);

        var started = await CallAsync(app, "import_document", new { filename = "blank.jpg", content = Png });
        var failed = await SettledAsync(app, started.GetProperty("importId").GetString()!);

        failed.GetProperty("status").GetString().Should().Be("failed");
        failed.GetProperty("error").GetString().Should().Contain("PDF processing failed");
        (await PagesChargedAsync(user)).Pages.Should().Be(1, "Mathpix had the page, so it billed it");
    }

    [Fact]
    public async Task A_file_named_png_that_is_not_an_image_is_refused_before_Mathpix_or_the_plan()
    {
        var user = NewId("fake");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var app = AppWith(api, user);

        var text = Convert.ToBase64String(Encoding.UTF8.GetBytes("These are my notes, saved as notes.png."));
        var refused = await CallAsync(app, "import_document", new { filename = "notes.png", content = text });
        refused.GetProperty("category").GetString().Should().Be("invalid");
        refused.GetProperty("error").GetString().Should().Contain("isn't a JPEG or PNG image");

        var notPdf = await CallAsync(app, "import_document", new { filename = "notes.pdf", content = Png });
        notPdf.GetProperty("error").GetString().Should().Contain("isn't a PDF");

        api.Requests.Should().BeEmpty();
        (await PagesChargedAsync(user)).Pages.Should().Be(0);
    }

    [Fact]
    public async Task An_image_over_10_MB_is_refused_and_so_is_any_file_over_the_connectors_limit()
    {
        var user = NewId("big");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var app = AppWith(api, user);

        // A PNG header, then 10 MB and one byte: under the connector's 14 million base64 characters, over the image limit.
        var big = new byte[Lilia.Api.Services.ScanImages.MaxBytes + 1];
        ScanImageImportTests.Png.CopyTo(big, 0);
        var image = await CallAsync(app, "import_document", new { filename = "photo.png", content = Convert.ToBase64String(big) });
        image.GetProperty("error").GetString().Should().Contain("over 10 MB");

        var huge = await CallAsync(app, "import_document", new { filename = "scan.pdf", content = new string('A', 14_000_001) });
        huge.GetProperty("error").GetString().Should().Contain("over 10 MB");
        api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task start_import_lists_PDF_and_images_and_the_drop_page_imports_a_scan()
    {
        var user = NewId("drop");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var host = HostWith(new MathpixTexZipTests.FakeMathpix());
        var app = ClientOf(host, user);
        var author = ClientOf(host, user, asApp: false);

        var start = await CallAsync(app, "start_import", new { });
        start.GetProperty("accepts").EnumerateArray().Select(a => a.GetString()).Should().Contain([".pdf", ".jpg", ".jpeg", ".png"]);
        var importId = start.GetProperty("importId").GetString()!;

        var post = await author.PostAsJsonAsync($"/api/connector/imports/{importId}", new { content = Png, format = "IMAGE", filename = "notes.png" });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        (await CallAsync(app, "import_status", new { importId })).GetProperty("status").GetString().Should().Be("done");
        (await PagesChargedAsync(user)).Pages.Should().Be(1);
    }

    [Fact]
    public async Task The_editors_upload_imports_a_scan_into_a_review_and_refuses_a_renamed_file()
    {
        var user = NewId("web");
        await SeedUserAsync(user);
        await GiveProAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var client = ClientOf(HostWith(api), user, asApp: false);

        var ok = await client.PostAsJsonAsync("/api/lilia/jobs/import", new { content = Png, format = "IMAGE", filename = "notes.png" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var sessionId = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reviewSessionId").GetString()!;
        var session = await client.GetFromJsonAsync<JsonElement>($"/api/lilia/import-review/sessions/{sessionId}");
        session.GetProperty("session").GetProperty("sourceFormat").GetString().Should().Be("image");

        var renamed = await client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes("not an image")),
            format = "IMAGE",
            filename = "notes.png",
        });
        renamed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await renamed.Content.ReadAsStringAsync()).Should().Contain("isn't a JPEG or PNG image");
        api.Requests.First().Should().Be("POST api.mathpix.com/v3/pdf");
        (await PagesChargedAsync(user)).Pages.Should().Be(1);
    }
}
