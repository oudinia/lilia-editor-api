using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// PDF import is not free (the owner, 11 Nov 2026): Mathpix reads each page at a price. Free is refused, whatever the
/// plan switch; Pro imports within its monthly pages; the site's monthly pages bound everyone; staff are not limited.
/// The plan is checked before the PDF goes anywhere.
/// </summary>
public class PdfImportPaidTests : IntegrationTestBase, IDisposable
{
    private const string Staff = "pdf_staff_account";
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public PdfImportPaidTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private WebApplicationFactory<Program> Factory(int sitePages = 2000)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Entitlements:StaffUserIds:0", Staff);
            b.UseSetting("Import:PdfSitePagesPerMonth", sitePages.ToString());
        });
        _factories.Add(factory);
        return factory;
    }

    private static string NewId(string what) => $"pdf_{what}_{Guid.NewGuid():N}"[..32];

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

    private static async Task<QuotaExceededException?> Refusal(WebApplicationFactory<Program> factory, string userId, int pages)
    {
        using var scope = factory.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        try
        {
            await plans.EnsureCanImportPdfAsync(userId, pages);
            return null;
        }
        catch (QuotaExceededException q)
        {
            return q;
        }
    }

    [Fact]
    public async Task The_plans_say_it_Pro_100_pages_a_month_Free_none()
    {
        await using var db = CreateDbContext();
        var caps = await db.Plans.Where(p => p.Slug == "pro" || p.Slug == "free").ToDictionaryAsync(p => p.Slug, p => p.Caps);
        caps["pro"].RootElement.GetProperty("pdfPagesPerMonth").GetInt32().Should().Be(100);
        caps["free"].RootElement.GetProperty("pdfPagesPerMonth").GetInt32().Should().Be(0);
        (await db.OperationKinds.SingleAsync(k => k.Code == "import.pdf")).PointsFor(12).Should().Be(17, "5, plus 1 a page");
    }

    [Fact]
    public async Task Free_is_refused_even_with_the_plan_limits_off()
    {
        var free = NewId("free");
        await SeedUserAsync(free);

        var refused = await Refusal(Factory(), free, 3);

        refused.Should().NotBeNull();
        refused!.Resource.Should().Be("pdfImport");
        refused.Message.Should().Contain("part of Pro");
    }

    [Fact]
    public async Task Pro_imports_within_its_monthly_pages_and_is_told_what_is_left()
    {
        var pro = NewId("pro");
        await SeedUserAsync(pro);
        await GivePlanAsync(pro, "pro");
        await PagesReadAsync(pro, 95);
        var factory = Factory();

        (await Refusal(factory, pro, 5)).Should().BeNull("95 + 5 is the 100 allowed");
        var over = await Refusal(factory, pro, 6);
        over!.Resource.Should().Be("pdfPagesPerMonth");
        over.Message.Should().Contain("6 pages").And.Contain("5 of this month's 100");
    }

    [Fact]
    public async Task The_site_s_monthly_pages_bound_everyone_but_staff()
    {
        var pro = NewId("site");
        await SeedUserAsync(pro);
        await GivePlanAsync(pro, "pro");
        // Whatever the other tests recorded this month, the site cap is just above it.
        await using (var db = CreateDbContext())
        {
            var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var all = await db.Operations.Where(o => o.Kind == "import.pdf" && o.CreatedAt >= start).SumAsync(o => (int?)o.Units) ?? 0;
            var factory = Factory(sitePages: all + 3);

            (await Refusal(factory, pro, 3)).Should().BeNull();
            (await Refusal(factory, pro, 4))!.Resource.Should().Be("pdfSitePages");
            (await Refusal(factory, Staff, 400)).Should().BeNull("staff are not limited");
        }
    }

    [Fact]
    public async Task A_free_PDF_import_is_answered_402_before_the_PDF_goes_anywhere()
    {
        var free = NewId("http");
        await SeedUserAsync(free);
        var client = Factory().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", free);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{free}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", free);

        var res = await client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = Convert.ToBase64String(Encoding.Latin1.GetBytes(TwoPagePdf)),
            format = "PDF",
            filename = "paper.pdf",
        });

        res.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().Should().Be("PLAN_IMPORT_LIMIT");
    }

    private const string TwoPagePdf = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n"
        + "2 0 obj << /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >> endobj\n"
        + "3 0 obj << /Type /Page /Parent 2 0 R >> endobj\n4 0 obj << /Type /Page /Parent 2 0 R >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF";

    [Theory]
    [InlineData(TwoPagePdf, 2)]
    [InlineData("%PDF-1.4\n3 0 obj << /Type /Page >> endobj\n4 0 obj << /Type /Page >> endobj\n5 0 obj << /Type /Page >> endobj\n%%EOF", 3)]
    [InlineData("%PDF-1.5\n(compressed page tree)\n%%EOF", 1)]
    public void The_pages_are_read_from_the_file(string pdf, int pages) =>
        PdfPages.Estimate(Encoding.Latin1.GetBytes(pdf)).Should().Be(pages);
}
