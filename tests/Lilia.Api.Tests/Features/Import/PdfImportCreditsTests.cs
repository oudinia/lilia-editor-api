using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The owner's pricing for PDF and scan import (20 Nov 2026): Free gets 10 PDF pages a month, and every page read costs
/// 10 credits (<c>Mathpix:CreditsPerPage</c>) from the connector's allowance: the day's first (Free 25, Pro 500), then
/// packs on Free, as tool calls are counted. A scanned image is one page. Both limits are checked from the file before
/// anything reaches Mathpix, so a refused import costs nothing; the charge is Mathpix's own page count. The editor's
/// upload and the connector charge the same.
///
/// <para>Plan limits on (<c>Entitlements:Enforce</c>, as on production). Mathpix is
/// <see cref="MathpixTexZipTests.FakeMathpix"/>, which reports 2 pages for any PDF.</para>
/// </summary>
public class PdfImportCreditsTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public PdfImportCreditsTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private static string NewId(string what) => $"cred_{what}_{Guid.NewGuid():N}"[..30];

    /// <summary>
    /// "%PDF-1.4 fake": nothing can count its pages from the file (the review of API #670), so it is checked as one page
    /// and charged what Mathpix reports, here 2, up to what is left.
    /// </summary>
    private static readonly string UncountablePdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 fake"));

    /// <summary>A real 2-page pdflatex PDF, its page tree compressed: counted 2 exactly.</summary>
    private static readonly string TwoPagePdf = Convert.ToBase64String(PdfPagesExactTests.TwoPagePdflatex);

    private static readonly string ThreePagePdf = Convert.ToBase64String(Encoding.Latin1.GetBytes(
        "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >> endobj\n%%EOF"));

    private static readonly string Png = Convert.ToBase64String(ScanImageImportTests.Png);

    private WebApplicationFactory<Program> Host(MathpixTexZipTests.FakeMathpix api, string? creditsPerPage = "10")
    {
        // Pages charged as actions (off by default since 23 Nov 2026): these tests cover the charge when it is on.
        var factory = Fixture.Factory.WithWebHostBuilder(builder => (creditsPerPage is null ? builder : builder.UseSetting("Mathpix:CreditsPerPage", creditsPerPage)).ConfigureServices(services =>
        {
            services.PostConfigure<EntitlementOptions>(o => { o.Enforce = true; o.StaffUserIds = []; });
            foreach (var d in services.Where(d => d.ServiceType == typeof(IPdfParser) || d.ServiceType == typeof(IScanImageParser)).ToList())
                services.Remove(d);
            services.AddScoped<IPdfParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
            services.AddScoped<IScanImageParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
        }));
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient ClientOf(WebApplicationFactory<Program> factory, string userId, bool asApp)
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

    private async Task GivePlanAsync(string userId, string slug)
    {
        await using var db = CreateDbContext();
        var plan = await db.Plans.SingleAsync(p => p.Slug == slug);
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task PagesReadAsync(string userId, int pages)
    {
        await using var db = CreateDbContext();
        db.Operations.Add(new Operation { Id = Guid.NewGuid(), UserId = userId, Kind = "import.pdf", Points = 5 + pages, Units = pages, Format = "pdf", Source = "screens" });
        await db.SaveChangesAsync();
    }

    private async Task BuyPackAsync(string userId)
    {
        await using var db = CreateDbContext();
        db.OperationCredits.Add(new OperationCredit { Id = Guid.NewGuid(), UserId = userId, Delta = OperationPacks.PerPack, Reason = "purchase", StripeSessionId = $"cs_test_{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<int> PagesThisMonthAsync(string userId)
    {
        await using var db = CreateDbContext();
        return await db.Operations.AsNoTracking().Where(o => o.UserId == userId && o.Kind == "import.pdf").SumAsync(o => o.Units ?? 0);
    }

    private async Task<int> PackBalanceAsync(string userId)
    {
        await using var db = CreateDbContext();
        return await db.OperationCredits.AsNoTracking().Where(c => c.UserId == userId).SumAsync(c => c.Delta);
    }

    private static async Task<int> CreditsUsedTodayAsync(WebApplicationFactory<Program> factory, string userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ConnectorAllowance>().UsedTodayAsync(userId);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient author, string content, string format, string filename) =>
        author.PostAsJsonAsync("/api/lilia/jobs/import", new { content, format, filename, skipReview = true });

    private static async Task<JsonElement> CallAsync(HttpClient app, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var text = await (await app.SendAsync(req)).Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    /// <summary>
    /// The connector's background import, followed on its ticket rather than with import_status, so the only connector
    /// action is the import_document call itself (each import_status would count one more).
    /// </summary>
    private static async Task<ConnectorTransfers.ImportTicket> SettledAsync(WebApplicationFactory<Program> factory, string userId, string importId)
    {
        var transfers = factory.Services.GetRequiredService<ConnectorTransfers>();
        var until = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var ticket = transfers.Import(importId, userId)!;
            if (ticket.Status != "working" || DateTime.UtcNow > until) return ticket;
            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task Free_imports_a_2_page_pdflatex_PDF_counted_2_for_20_credits_and_has_8_pages_left_this_month()
    {
        var user = NewId("two");
        await SeedUserAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var host = Host(api);

        var res = await UploadAsync(ClientOf(host, user, asApp: false), TwoPagePdf, "PDF", "notes.pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        (await CreditsUsedTodayAsync(host, user)).Should().Be(20);
        (await PagesThisMonthAsync(user)).Should().Be(2, "8 of Free's 10 are left");
    }

    // The owner, 23 Nov 2026 (Grok's bulk retest): by default a PDF page counts once, against the month's PDF pages, and
    // costs no daily connector actions; a 3-page PDF Free used to be refused on credits now goes through.
    [Fact]
    public async Task By_default_PDF_pages_count_against_the_month_only_not_the_day_s_actions()
    {
        var user = NewId("monthly");
        await SeedUserAsync(user);
        var host = Host(new MathpixTexZipTests.FakeMathpix(), creditsPerPage: null);

        var res = await UploadAsync(ClientOf(host, user, asApp: false), TwoPagePdf, "PDF", "notes.pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await CreditsUsedTodayAsync(host, user)).Should().Be(0);
        (await PagesThisMonthAsync(user)).Should().Be(2);

        (await UploadAsync(ClientOf(host, user, asApp: false), ThreePagePdf, "PDF", "notes.pdf")).StatusCode.Should().Be(HttpStatusCode.OK,
            "3 pages fit the month's 8 left, and no longer need 30 actions");
        // The month counts the pages read (the fake reader reads two of each file): still no day's actions.
        (await PagesThisMonthAsync(user)).Should().BeGreaterThan(2);
        (await CreditsUsedTodayAsync(host, user)).Should().Be(0);
    }

    [Fact]
    public async Task A_PDF_whose_pages_cannot_be_counted_goes_to_Mathpix_and_is_charged_its_pages_up_to_what_is_left()
    {
        var plenty = NewId("unk");
        var nearly = NewId("unk9");
        await SeedUserAsync(plenty);
        await SeedUserAsync(nearly);
        await PagesReadAsync(nearly, 9);
        var api = new MathpixTexZipTests.FakeMathpix();
        var host = Host(api);

        // Checked as one page; Mathpix reads 2 and they are charged.
        (await UploadAsync(ClientOf(host, plenty, asApp: false), UncountablePdf, "PDF", "notes.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PagesThisMonthAsync(plenty)).Should().Be(2);
        (await CreditsUsedTodayAsync(host, plenty)).Should().Be(20);
        api.Requests.Should().Contain("POST api.mathpix.com/v3/pdf");

        // With 1 page left this month it isn't refused, and the charge stops at that page.
        (await UploadAsync(ClientOf(host, nearly, asApp: false), UncountablePdf, "PDF", "notes.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PagesThisMonthAsync(nearly)).Should().Be(10);
        (await CreditsUsedTodayAsync(host, nearly)).Should().Be(10);
    }

    [Fact]
    public async Task Free_is_refused_a_3_page_PDF_on_credits_before_Mathpix_on_the_web_and_through_the_connector()
    {
        var user = NewId("three");
        await SeedUserAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var host = Host(api);

        var web = await UploadAsync(ClientOf(host, user, asApp: false), ThreePagePdf, "PDF", "notes.pdf");
        web.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var webBody = await web.Content.ReadAsStringAsync();
        webBody.Should().Contain("PLAN_PDF_IMPORT").And.Contain("This 3-page PDF uses 30 credits (10 a page); you have 25 left today.");

        var app = await CallAsync(ClientOf(host, user, asApp: true), "import_document", new { filename = "notes.pdf", content = ThreePagePdf });
        app.GetProperty("category").GetString().Should().Be("plan-limit");
        app.GetProperty("limit").GetProperty("resource").GetString().Should().Be("pdfCredits");
        // The import_document call is itself one of the day's actions.
        app.GetProperty("error").GetString().Should().Contain("This 3-page PDF uses 30 credits").And.Contain("you have 24 left today")
            .And.Contain("Try a shorter PDF, buy a pack").And.Contain("or upgrade");

        api.Requests.Should().BeEmpty("a refused import costs nothing");
        (await PagesThisMonthAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task Free_is_refused_the_11th_page_of_the_month_on_the_cap()
    {
        var user = NewId("eleven");
        await SeedUserAsync(user);
        await PagesReadAsync(user, 10);
        var api = new MathpixTexZipTests.FakeMathpix();
        var host = Host(api);

        var res = await UploadAsync(ClientOf(host, user, asApp: false), Png, "IMAGE", "notes.png");

        res.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await res.Content.ReadAsStringAsync()).Should().Contain("This month's 10 PDF pages are used (10 a month on");
        api.Requests.Should().BeEmpty();
        (await CreditsUsedTodayAsync(host, user)).Should().Be(0);
    }

    [Fact]
    public async Task Pro_has_100_pages_a_month_at_10_credits_a_page()
    {
        var user = NewId("pro");
        await SeedUserAsync(user);
        await GivePlanAsync(user, "pro");
        await PagesReadAsync(user, 97);
        var api = new MathpixTexZipTests.FakeMathpix();
        var host = Host(api);
        var author = ClientOf(host, user, asApp: false);

        (await UploadAsync(author, TwoPagePdf, "PDF", "notes.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreditsUsedTodayAsync(host, user)).Should().Be(20, "2 pages, 10 credits each");
        (await PagesThisMonthAsync(user)).Should().Be(99);

        (await UploadAsync(author, Png, "IMAGE", "notes.png")).StatusCode.Should().Be(HttpStatusCode.OK, "the 100th page");
        (await CreditsUsedTodayAsync(host, user)).Should().Be(30);

        var over = await UploadAsync(author, Png, "IMAGE", "notes.png");
        over.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await over.Content.ReadAsStringAsync()).Should().Contain("100 a month on Pro");
    }

    [Fact]
    public async Task Past_Free_s_daily_credits_a_scanned_page_is_paid_from_a_pack()
    {
        var user = NewId("pack");
        await SeedUserAsync(user);
        await BuyPackAsync(user);
        var api = new MathpixTexZipTests.FakeMathpix();
        var host = Host(api);
        var author = ClientOf(host, user, asApp: false);

        (await UploadAsync(author, TwoPagePdf, "PDF", "notes.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreditsUsedTodayAsync(host, user)).Should().Be(20);
        (await PackBalanceAsync(user)).Should().Be(OperationPacks.PerPack, "the day's allowance went first");

        // 10 credits with 5 left today: 5 from the day, 5 from the pack.
        (await UploadAsync(author, Png, "IMAGE", "notes.png")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreditsUsedTodayAsync(host, user)).Should().Be(25);
        (await PackBalanceAsync(user)).Should().Be(OperationPacks.PerPack - 5);
        (await PagesThisMonthAsync(user)).Should().Be(3);
    }

    [Fact]
    public async Task The_web_upload_and_the_connector_charge_a_PDF_the_same()
    {
        var onWeb = NewId("web");
        var onApp = NewId("app");
        await SeedUserAsync(onWeb);
        await SeedUserAsync(onApp);
        var host = Host(new MathpixTexZipTests.FakeMathpix());

        (await UploadAsync(ClientOf(host, onWeb, asApp: false), TwoPagePdf, "PDF", "notes.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

        var started = await CallAsync(ClientOf(host, onApp, asApp: true), "import_document", new { filename = "notes.pdf", content = TwoPagePdf });
        started.TryGetProperty("error", out _).Should().BeFalse(started.ToString());
        started.GetProperty("pages").GetInt32().Should().Be(2);
        started.GetProperty("credits").GetInt32().Should().Be(20);
        (await SettledAsync(host, onApp, started.GetProperty("importId").GetString()!)).Status.Should().Be("done");

        (await CreditsUsedTodayAsync(host, onWeb)).Should().Be(20);
        (await CreditsUsedTodayAsync(host, onApp)).Should().Be(1 + 20, "the import_document call is one action, as every tool call; the pages the same 20");
        (await PagesThisMonthAsync(onWeb)).Should().Be(2);
        (await PagesThisMonthAsync(onApp)).Should().Be(2);
    }
}
