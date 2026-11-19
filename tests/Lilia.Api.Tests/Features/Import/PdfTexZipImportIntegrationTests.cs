using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// PDF import via tex.zip, 19 Nov 2026, end to end through <c>POST /api/lilia/jobs/import</c>: a PDF whose tex.zip
/// Mathpix made is imported as an uploaded Overleaf zip is (LaTeX parser, review, finalize storing the figures as
/// files), the review still titled as a PDF import; when the tex.zip fails, the Markdown is imported as before. The
/// pages Mathpix read are counted as before.
///
/// <para>Mathpix is <see cref="MathpixTexZipTests.FakeMathpix"/> behind the real client and import service: no file
/// leaves the machine.</para>
/// </summary>
public class PdfTexZipImportIntegrationTests : IntegrationTestBase
{
    private readonly string _userId = $"pdftex-{Guid.NewGuid():N}"[..28];

    public PdfTexZipImportIntegrationTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        // PDF import is part of Pro (11 Nov 2026).
        await using var db = CreateDbContext();
        var pro = db.Plans.Single(p => p.Slug == "pro");
        db.UserPlans.Add(new Lilia.Core.Entities.UserPlan { Id = Guid.NewGuid(), UserId = _userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private (WebApplicationFactory<Program> Factory, HttpClient Client) WithMathpix(MathpixTexZipTests.FakeMathpix api)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPdfParser));
            if (descriptor != null) services.Remove(descriptor);
            services.AddScoped<IPdfParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", _userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{_userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", _userId);
        return (factory, client);
    }

    private static Task<HttpResponseMessage> ImportPdfAsync(HttpClient client, bool skipReview = false) =>
        client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 fake")),
            format = "PDF",
            filename = "attention.pdf",
            skipReview,
        });

    private static async Task<(JsonElement Session, List<JsonElement> Blocks)> SessionAsync(HttpClient client, string sessionId)
    {
        var data = await client.GetFromJsonAsync<JsonElement>($"/api/lilia/import-review/sessions/{sessionId}");
        return (data.GetProperty("session"), data.GetProperty("blocks").EnumerateArray().ToList());
    }

    [Fact]
    public async Task A_PDF_with_a_ready_tex_zip_is_reviewed_as_its_LaTeX_and_finalize_stores_its_figure_as_a_file()
    {
        var api = new MathpixTexZipTests.FakeMathpix();
        var (factory, client) = WithMathpix(api);
        await using var _ = factory;

        var response = await ImportPdfAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = result.GetProperty("reviewSessionId").GetString()!;

        // The review is ready and titled as a PDF import; its blocks are the LaTeX parser's.
        var (session, blocks) = await SessionAsync(client, sessionId);
        session.GetProperty("sourceFormat").GetString().Should().Be("pdf");
        session.GetProperty("status").GetString().Should().Be("pending_review");
        blocks.Should().Contain(b => b.GetProperty("originalType").GetString() == "heading"
                                     && b.GetProperty("originalContent").GetRawText().Contains("Introduction"));
        blocks.Should().Contain(b => b.GetProperty("originalContent").GetRawText().Contains("read from the tex.zip"));
        blocks.Should().NotContain(b => b.GetProperty("originalContent").GetRawText().Contains("read from the Markdown"));
        blocks.Should().Contain(b => b.GetProperty("originalType").GetString() == "equation");
        blocks.Should().Contain(b => b.GetProperty("originalType").GetString() == "figure");

        var finalize = await client.PostAsJsonAsync($"/api/lilia/import-review/sessions/{sessionId}/finalize", new { force = true });
        finalize.StatusCode.Should().Be(HttpStatusCode.OK, await finalize.Content.ReadAsStringAsync());
        var documentId = (await finalize.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("document").GetProperty("id").GetGuid();

        await using var db = CreateDbContext();
        // The figure is a file on the document, and the figure block points at it, as an Overleaf zip's does.
        var asset = await db.Assets.SingleAsync(a => a.DocumentId == documentId && a.FileName == $"{MathpixTexZipTests.PdfId}-1.jpg");
        var figure = await db.Blocks.SingleAsync(b => b.DocumentId == documentId && b.Type == "figure");
        figure.Content.RootElement.GetProperty("src").GetString().Should().Be(asset.Url);
        // The pages Mathpix read count against the plan, as before.
        (await db.Operations.Where(o => o.UserId == _userId && o.Kind == "import.pdf").SumAsync(o => o.Units)).Should().Be(2);
        // And the PDF is gone from Mathpix.
        api.Requests.Should().Contain($"DELETE api.mathpix.com/v3/pdf/{MathpixTexZipTests.PdfId}");
    }

    [Fact]
    public async Task A_PDF_imported_from_its_tex_zip_without_review_answers_with_its_document()
    {
        var (factory, client) = WithMathpix(new MathpixTexZipTests.FakeMathpix());
        await using var _ = factory;

        var response = await ImportPdfAsync(client, skipReview: true);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        var documentId = result.GetProperty("document").GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        (await db.Blocks.CountAsync(b => b.DocumentId == documentId && b.Type == "heading")).Should().BeGreaterThan(0);
        (await db.Assets.CountAsync(a => a.DocumentId == documentId)).Should().Be(1);
    }

    [Fact]
    public async Task A_PDF_whose_tex_zip_failed_is_imported_from_the_Markdown_as_before()
    {
        var api = new MathpixTexZipTests.FakeMathpix { TexZipStatuses = new(["error"]) };
        var (factory, client) = WithMathpix(api);
        await using var _ = factory;

        var response = await ImportPdfAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var sessionId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reviewSessionId").GetString()!;

        var (session, blocks) = await SessionAsync(client, sessionId);
        session.GetProperty("sourceFormat").GetString().Should().Be("pdf");
        blocks.Should().Contain(b => b.GetProperty("originalContent").GetRawText().Contains("read from the Markdown"));
        blocks.Should().NotContain(b => b.GetProperty("originalContent").GetRawText().Contains("read from the tex.zip"));
        api.Requests.Should().Contain($"DELETE api.mathpix.com/v3/pdf/{MathpixTexZipTests.PdfId}");
    }
}
