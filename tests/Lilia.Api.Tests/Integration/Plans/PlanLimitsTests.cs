using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Plans;

/// <summary>
/// The free plan holds 3 documents (decided 14 Oct 2026). The limit is documents, never output, and
/// nothing is created until there's room. Limits are off until <c>Entitlements:Enforce</c> is set,
/// and staff accounts (the owner's, which holds the connector's test documents) hold Beta.
/// </summary>
[Collection("Integration")]
public class PlanLimitsTests : IntegrationTestBase
{
    private const string Staff = "plan-staff";
    private readonly WebApplicationFactory<Program> _enforced;

    public PlanLimitsTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _enforced = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.PostConfigure<EntitlementOptions>(o => { o.Enforce = true; o.StaffUserIds = [Staff]; })));
    }

    private static HttpClient As(WebApplicationFactory<Program> factory, string userId)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private async Task<string> FreeAuthorWith(int documents)
    {
        var id = $"plan-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id);
        for (var i = 0; i < documents; i++) await SeedDocumentAsync(id, $"Paper {i + 1}");
        return id;
    }

    private async Task<int> OwnedCount(string userId)
    {
        await using var db = CreateDbContext();
        return await db.Documents.CountAsync(d => d.OwnerId == userId);
    }

    private static async Task<string> ErrorCode(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task With_limits_off_a_fourth_document_is_made_and_the_plan_still_reports_the_count()
    {
        var author = await FreeAuthorWith(3);
        var client = As(Fixture.Factory, author);

        (await client.PostAsJsonAsync("/api/documents", new { title = "Fourth" })).StatusCode
            .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var plan = await client.GetFromJsonAsync<JsonElement>("/api/plan");
        plan.GetProperty("enforced").GetBoolean().Should().BeFalse();
        plan.GetProperty("plan").GetProperty("slug").GetString().Should().Be("free");
        plan.GetProperty("documents").GetProperty("used").GetInt32().Should().Be(4);
        plan.GetProperty("documents").GetProperty("limit").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task At_three_documents_a_fourth_is_refused_and_nothing_is_created()
    {
        var author = await FreeAuthorWith(3);
        var client = As(_enforced, author);

        var create = await client.PostAsJsonAsync("/api/documents", new { title = "Fourth" });
        create.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await ErrorCode(create)).Should().Be("PLAN_DOCUMENT_LIMIT");

        await using var db = CreateDbContext();
        var first = await db.Documents.FirstAsync(d => d.OwnerId == author);
        var duplicate = await client.PostAsync($"/api/documents/{first.Id}/duplicate", null);
        duplicate.StatusCode.Should().Be(HttpStatusCode.PaymentRequired, "making a copy starts a document too");

        var import = await client.PostAsJsonAsync("/api/lilia/jobs/import",
            new { content = "@paragraph\n  Hello.", format = "LML", filename = "notes.lml" });
        import.StatusCode.Should().Be(HttpStatusCode.PaymentRequired, "an import makes a document");

        (await OwnedCount(author)).Should().Be(3);
    }

    [Fact]
    public async Task Documents_in_the_trash_and_documents_shared_with_you_do_not_count()
    {
        var author = await FreeAuthorWith(3);
        await FreeAuthorWith(1); // someone else's document, as one shared with you is: not yours, so not counted
        await using (var db = CreateDbContext())
        {
            var mine = await db.Documents.FirstAsync(d => d.OwnerId == author);
            mine.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = As(_enforced, author);
        var plan = await client.GetFromJsonAsync<JsonElement>("/api/plan");
        plan.GetProperty("documents").GetProperty("used").GetInt32().Should().Be(2);

        (await client.PostAsJsonAsync("/api/documents", new { title = "Third" })).StatusCode
            .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/documents", new { title = "Fourth" })).StatusCode
            .Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task A_staff_account_holds_beta_and_is_not_limited()
    {
        await using (var db = CreateDbContext())
        {
            if (!await db.Users.AnyAsync(u => u.Id == Staff)) await SeedUserAsync(Staff);
        }
        for (var i = 0; i < 4; i++) await SeedDocumentAsync(Staff, $"LTX-{i}");
        var client = As(_enforced, Staff);

        (await client.PostAsJsonAsync("/api/documents", new { title = "LTX-next" })).StatusCode
            .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var plan = await client.GetFromJsonAsync<JsonElement>("/api/plan");
        plan.GetProperty("plan").GetProperty("slug").GetString().Should().Be("beta");
        plan.GetProperty("documents").GetProperty("limit").GetInt32().Should().Be(-1);
    }

    [Fact]
    public async Task Ask_Lilia_is_how_far_through_this_months_allowance_and_last_month_does_not_count()
    {
        var author = await FreeAuthorWith(0);
        await using (var db = CreateDbContext())
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            db.AiCreditLedger.AddRange(
                new AiCreditLedger { Id = Guid.NewGuid(), UserId = author, Delta = -500, Reason = "spend", CreatedAt = monthStart.AddDays(-3) },
                new AiCreditLedger { Id = Guid.NewGuid(), UserId = author, Delta = -50, Reason = "spend", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var plan = await As(_enforced, author).GetFromJsonAsync<JsonElement>("/api/plan");
        var ask = plan.GetProperty("ask");
        ask.GetProperty("fraction").GetDouble().Should().BeApproximately(0.5, 0.001, "50 of Free's 100 this month");
        ask.TryGetProperty("used", out _).Should().BeFalse("Ask Lilia is an allowance, never a balance");
        ask.GetProperty("resetsAt").GetDateTime().Day.Should().Be(1);
    }

    [Fact]
    public async Task The_plan_says_whether_a_used_Ask_allowance_stops_Ask_Lilia()
    {
        var author = await FreeAuthorWith(0);
        var off = await As(_enforced, author).GetFromJsonAsync<JsonElement>("/api/plan");
        off.GetProperty("ask").GetProperty("enforced").GetBoolean().Should().BeFalse("AI:EnforceCredits is off by default");

        var aiOn = _enforced.WithWebHostBuilder(b => b.UseSetting("AI:EnforceCredits", "true"));
        var on = await As(aiOn, author).GetFromJsonAsync<JsonElement>("/api/plan");
        on.GetProperty("ask").GetProperty("enforced").GetBoolean().Should().BeTrue();
    }
}
