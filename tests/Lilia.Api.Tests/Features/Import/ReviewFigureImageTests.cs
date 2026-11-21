using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's retest of API #672 (20 Nov 2026): the review of a scanned page showed each sketch as a grey "image"
/// box, though the finished document showed the image. Before finalize, a figure's src is only the name its
/// <c>\includegraphics</c> gave (Mathpix's <c>2026_11_19_…-1</c>); the image is in the zip the import kept, and finalize
/// stores it as an asset. <c>GET …/sessions/{id}/figure-image?src=…</c> now serves it from that zip for the review, to the
/// session's people only. Mathpix is <see cref="MathpixTexZipTests.FakeMathpix"/>: nothing leaves the machine.
/// </summary>
public class ReviewFigureImageTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    private readonly string _userId = $"revfig-{Guid.NewGuid():N}"[..27];

    public ReviewFigureImageTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        await using var db = CreateDbContext();
        var pro = db.Plans.Single(p => p.Slug == "pro");
        db.UserPlans.Add(new Lilia.Core.Entities.UserPlan { Id = Guid.NewGuid(), UserId = _userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>The app's local storage, counting the reads of kept import zips.</summary>
    private sealed class CountingStorage(Lilia.Core.Interfaces.IStorageService inner) : Lilia.Core.Interfaces.IStorageService
    {
        private int _zipReads;
        public int ZipReads => Volatile.Read(ref _zipReads);
        public Task<string> UploadAsync(string key, Stream content, string contentType) => inner.UploadAsync(key, content, contentType);
        public async Task<Stream> DownloadAsync(string key)
        {
            if (key.StartsWith("imports/", StringComparison.Ordinal) && key.EndsWith(".zip", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _zipReads);
                await Task.Delay(200); // a slow download, so parallel requests overlap
            }
            return await inner.DownloadAsync(key);
        }
        public Task DeleteAsync(string key) => inner.DeleteAsync(key);
        public string GetPublicUrl(string key) => inner.GetPublicUrl(key);
        public Task<string> GeneratePresignedUploadUrl(string key, string contentType, TimeSpan? expiry = null) =>
            inner.GeneratePresignedUploadUrl(key, contentType, expiry);
    }

    private CountingStorage? _storage;

    private WebApplicationFactory<Program> Host(MathpixTexZipTests.FakeMathpix api)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton<Lilia.Core.Interfaces.IStorageService>(sp =>
                _storage ??= new CountingStorage(new Lilia.Api.Services.LocalStorageService(sp.GetRequiredService<IConfiguration>())));
            foreach (var d in services.Where(d => d.ServiceType == typeof(IPdfParser) || d.ServiceType == typeof(IScanImageParser)).ToList())
                services.Remove(d);
            services.AddScoped<IPdfParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
            services.AddScoped<IScanImageParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
        }));
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient As(WebApplicationFactory<Program> factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return client;
    }

    private static readonly string ScanPng = Convert.ToBase64String(ScanImageImportTests.Image(SkiaSharp.SKEncodedImageFormat.Png, 65, 86));

    /// <summary>Imports the scan with review (the editor's upload): the session and its figure blocks' src.</summary>
    private async Task<(Guid SessionId, List<string> Sources)> ImportForReviewAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/lilia/jobs/import", new { content = ScanPng, format = "IMAGE", filename = "handwritten.png" });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var sessionId = Guid.Parse((await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("reviewSessionId").GetString()!);
        await using var db = CreateDbContext();
        var figures = await db.ImportBlockReviews.AsNoTracking()
            .Where(b => b.SessionId == sessionId && (b.CurrentType ?? b.OriginalType) == "figure")
            .OrderBy(b => b.SortOrder).ToListAsync();
        return (sessionId, figures.Select(b => (b.CurrentContent ?? b.OriginalContent).RootElement.GetProperty("src").GetString() ?? "").ToList());
    }

    private static string Url(Guid sessionId, string src) =>
        $"/api/lilia/import-review/sessions/{sessionId}/figure-image?src={Uri.EscapeDataString(src)}";

    [Fact]
    public async Task A_scans_figure_is_served_from_the_kept_zip_before_finalize_to_its_owner_only()
    {
        var factory = Host(new MathpixTexZipTests.FakeMathpix
        {
            TexZipDownload = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ScanFiguresAndSymbolsTests.HandwrittenTexZip()) },
        });
        var client = As(factory, _userId);
        var (sessionId, sources) = await ImportForReviewAsync(client);

        // The review's figures name project files, not addresses a browser can load.
        sources.Should().HaveCount(2);
        sources.Should().OnlyContain(s => s.Length > 0 && !s.StartsWith("http") && !s.StartsWith("data:") && !s.StartsWith("/"));

        foreach (var src in sources)
        {
            var image = await client.GetAsync(Url(sessionId, src));
            image.StatusCode.Should().Be(HttpStatusCode.OK, src);
            image.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
            image.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
            (await image.Content.ReadAsByteArrayAsync()).Should().Equal(ScanFiguresAndSymbolsTests.Sketch);
        }

        // A name the zip doesn't have, a non-image, and someone else: nothing.
        (await client.GetAsync(Url(sessionId, "no-such-figure"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Url(sessionId, $"{MathpixTexZipTests.PdfId}.tex"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var stranger = $"revfig-x-{Guid.NewGuid():N}"[..27];
        await SeedUserAsync(stranger);
        (await As(factory, stranger).GetAsync(Url(sessionId, sources[0]))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_review_without_a_kept_zip_answers_404_and_its_figures_carry_the_image_inline()
    {
        // The Markdown route: Mathpix's cropped figure is fetched at import and kept in the block as a data: URL.
        var factory = Host(new MathpixTexZipTests.FakeMathpix
        {
            TexZipStatuses = new(["error"]),
            MarkdownBody = ScanFiguresAndSymbolsTests.HandwrittenMarkdown,
            CdnFigure = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ScanFiguresAndSymbolsTests.Sketch) },
        });
        var client = As(factory, _userId);
        var (sessionId, sources) = await ImportForReviewAsync(client);

        sources.Should().Contain(s => s.StartsWith("data:image/jpeg;base64,"));
        (await client.GetAsync(Url(sessionId, "2026_11_19_0a1b2c3d4e5f-1"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> TexZipHost() => Host(new MathpixTexZipTests.FakeMathpix
    {
        TexZipDownload = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ScanFiguresAndSymbolsTests.HandwrittenTexZip()) },
    });

    /// <summary>
    /// The review asks for every figure at once (the review of API #674, 20 Nov 2026): one download and one extraction
    /// of the kept zip for all of them, and none for the next requests while its images are kept.
    /// </summary>
    [Fact]
    public async Task Parallel_requests_for_a_reviews_figures_read_the_kept_zip_once()
    {
        var client = As(TexZipHost(), _userId);
        var (sessionId, sources) = await ImportForReviewAsync(client);
        var before = _storage!.ZipReads;

        var answers = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => client.GetAsync(Url(sessionId, sources[i % sources.Count]))));
        answers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.OK);
        foreach (var a in answers)
            (await a.Content.ReadAsByteArrayAsync()).Should().Equal(ScanFiguresAndSymbolsTests.Sketch);
        (_storage.ZipReads - before).Should().Be(1, "twelve parallel requests share one read of the zip");

        (await client.GetAsync(Url(sessionId, sources[0]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (_storage.ZipReads - before).Should().Be(1, "the images are kept for the next requests");
    }

    [Fact]
    public async Task A_kept_zip_over_the_imports_limit_is_not_read()
    {
        var factory = TexZipHost();
        var client = As(factory, _userId);
        var (sessionId, sources) = await ImportForReviewAsync(client);
        await using (var db = CreateDbContext())
        {
            var jobId = await db.ImportReviewSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => s.JobId).SingleAsync();
            // Bigger than any import route takes: the review keeps its placeholder rather than reading it.
            using var big = new MemoryStream(new byte[Lilia.Api.Services.ImportReviewService.MaxReviewZipBytes + 1]);
            await _storage!.UploadAsync($"imports/{jobId}.zip", big, "application/zip");
        }

        (await client.GetAsync(Url(sessionId, sources[0]))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
