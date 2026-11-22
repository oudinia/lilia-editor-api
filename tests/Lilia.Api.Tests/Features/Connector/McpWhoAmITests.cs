using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// 22 Nov 2026, launch-bot #269: <c>whoami</c> answers "am I on Free?" from Claude with the token's own account only (a
/// name, a masked email hint, the plan, today's allowance, the month's limits, the pricing link) and never the full email
/// or an internal id; and a plan-limit error names the limit, its renewal and ends with the pricing link. The plan limits
/// are on in these hosts, as in production.
/// </summary>
public class McpWhoAmITests : IntegrationTestBase, IDisposable
{
    private const string Pricing = "https://liliaeditor.com/#pricing";
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public McpWhoAmITests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

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
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    /// <summary>A connected app for this account, on a host with the plan limits on.</summary>
    private HttpClient App(string userId, string email, string name)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Entitlements:Enforce", "true");
            b.UseSetting("Mcp:CallsPerMinute", "1000");
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", email);
        c.DefaultRequestHeaders.Add("X-Test-Name", name);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        c.DefaultRequestHeaders.Add("X-Test-Scope", "openid documents:read documents:write");
        return c;
    }

    private async Task GivePlanAsync(string userId, string slug)
    {
        await using var db = CreateDbContext();
        var plan = await db.Plans.SingleAsync(p => p.Slug == slug);
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<int> FreeCapAsync(string key)
    {
        await using var db = CreateDbContext();
        var free = await db.Plans.AsNoTracking().SingleAsync(p => p.Slug == "free");
        return free.Caps.RootElement.GetProperty(key).GetInt32();
    }

    private static string NewId(string what) => $"whoami_{what}_{Guid.NewGuid():N}"[..36];

    /// <summary>A unique address (emails are unique) that still masks to a•••@example.org.</summary>
    private static string NewEmail() => $"ada.lovelace.{Guid.NewGuid():N}"[..24] + "@example.org";

    /// <summary>The reply as an app reads it, unescaped, so a leak can't hide behind \u escapes.</summary>
    private static string Plain(JsonElement e) =>
        JsonSerializer.Serialize(e, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static void ShouldNotLeak(JsonElement reply, string userId, string email)
    {
        var text = Plain(reply);
        text.Should().NotContain(email).And.NotContain(userId).And.NotContain("ada.lovelace").And.NotContain("stripe", "no billing ids");
        text.Should().NotContain("token").And.NotContain("session");
    }

    [Fact]
    public async Task Whoami_on_Free_gives_the_plan_the_allowance_and_a_masked_email_and_nothing_private()
    {
        var id = NewId("free");
        var email = NewEmail();
        await SeedUserAsync(id, email, "Ada Lovelace");
        var app = App(id, email, "Ada Lovelace");
        // Two counted actions first.
        (await CallAsync(app, "list_documents", new { })).TryGetProperty("error", out _).Should().BeFalse();
        (await CallAsync(app, "list_documents", new { })).TryGetProperty("error", out _).Should().BeFalse();

        var me = await CallAsync(app, "whoami", new { });
        me.TryGetProperty("error", out _).Should().BeFalse(me.ToString());
        me.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        me.GetProperty("emailHint").GetString().Should().Be("a•••@example.org");
        me.GetProperty("plan").GetProperty("slug").GetString().Should().Be("free");
        me.GetProperty("plan").GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();

        var cap = await FreeCapAsync(ConnectorAllowance.CapKey);
        var c = me.GetProperty("connector");
        c.GetProperty("dailyCap").GetInt32().Should().Be(cap);
        c.GetProperty("usedToday").GetInt32().Should().Be(2, "whoami itself doesn't count");
        c.GetProperty("leftToday").GetInt32().Should().Be(cap - 2);
        c.GetProperty("operationPacksLeft").GetInt32().Should().Be(0);
        c.GetProperty("resetsAt").GetDateTime().ToUniversalTime().Should().Be(DateTime.UtcNow.Date.AddDays(1));

        var month = me.GetProperty("month");
        month.GetProperty("documents").GetProperty("used").GetInt32().Should().Be(0);
        month.GetProperty("documents").GetProperty("limit").GetInt32().Should().Be(await FreeCapAsync("maxDocs"));
        month.GetProperty("imports").GetProperty("limit").GetInt32().Should().Be(await FreeCapAsync("maxImportsPerMonth"));
        month.GetProperty("pdfPages").GetProperty("limit").GetInt32().Should().Be(await FreeCapAsync("pdfPagesPerMonth"));
        // Ask Lilia is an allowance, never a balance: a fraction, no credit numbers.
        var ask = month.GetProperty("askLilia");
        ask.GetProperty("fractionUsed").GetDouble().Should().BeInRange(0, 1);
        Plain(ask).Should().NotContain("credit");

        me.GetProperty("paidPlanTools").GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
        me.GetProperty("pricing").GetString().Should().Be(Pricing);
        ShouldNotLeak(me, id, email);
    }

    [Fact]
    public async Task Whoami_on_Pro_names_Pro_and_its_safety_cap_without_packs_or_private_details()
    {
        var id = NewId("pro");
        var email = NewEmail();
        await SeedUserAsync(id, email, "Ada Lovelace");
        await GivePlanAsync(id, "pro");
        var app = App(id, email, "Ada Lovelace");
        (await CallAsync(app, "list_documents", new { })).TryGetProperty("error", out _).Should().BeFalse();

        var me = await CallAsync(app, "whoami", new { });
        me.GetProperty("plan").GetProperty("slug").GetString().Should().Be("pro");
        me.GetProperty("emailHint").GetString().Should().Be("a•••@example.org");
        var c = me.GetProperty("connector");
        c.GetProperty("dailyCap").GetInt32().Should().Be(500);
        c.GetProperty("usedToday").GetInt32().Should().Be(1);
        c.GetProperty("leftToday").GetInt32().Should().Be(499);
        // Packs are Free's: none on Pro (the connector leaves null fields out).
        (c.TryGetProperty("operationPacksLeft", out var packs) && packs.ValueKind != JsonValueKind.Null).Should().BeFalse();
        c.GetProperty("note").GetString().Should().Contain("safety limit");
        me.GetProperty("paidPlanTools").GetProperty("tools").GetArrayLength().Should().Be(0);
        me.GetProperty("pricing").GetString().Should().Be(Pricing);
        ShouldNotLeak(me, id, email);
    }

    [Fact]
    public async Task A_plan_limit_names_the_limit_its_renewal_and_ends_with_the_pricing_link_with_no_account_details()
    {
        var id = NewId("limit");
        var email = NewEmail();
        await SeedUserAsync(id, email, "Ada Lovelace");
        var maxDocs = await FreeCapAsync("maxDocs");
        for (var i = 0; i < maxDocs; i++) await SeedDocumentAsync(id, $"Paper {i}");
        var app = App(id, email, "Ada Lovelace");

        // No renewal date: the documents a plan holds.
        var docs = await CallAsync(app, "create_document", new { title = "One too many" });
        docs.GetProperty("category").GetString().Should().Be("plan-limit");
        docs.GetProperty("error").GetString().Should().Contain($"holds {maxDocs} documents").And.EndWith($"Plans and prices: {Pricing}");
        ShouldNotLeak(docs, id, email);

        // A monthly limit (another account, whose documents leave room): its renewal date, then the link.
        var other = NewId("pages");
        var otherEmail = NewEmail();
        await SeedUserAsync(other, otherEmail, "Ada Lovelace");
        var pages = await FreeCapAsync("pdfPagesPerMonth");
        await using (var db = CreateDbContext())
        {
            db.Operations.Add(new Lilia.Core.Entities.Operation { Id = Guid.NewGuid(), UserId = other, Kind = "import.pdf", Points = 15, Units = Math.Max(1, pages), Format = "pdf", Source = "screens" });
            await db.SaveChangesAsync();
        }
        var pdf = await CallAsync(App(other, otherEmail, "Ada Lovelace"), "import_document",
            new { filename = "notes.pdf", content = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 fake")) });
        pdf.GetProperty("category").GetString().Should().Be("plan-limit");
        var renews = pdf.GetProperty("limit").GetProperty("resetsAt").GetDateTime();
        pdf.GetProperty("error").GetString().Should().Contain(renews.ToString("d MMMM")).And.EndWith($"Plans and prices: {Pricing}");
        ShouldNotLeak(pdf, other, otherEmail);
    }

    [Fact]
    public void A_limit_without_its_date_in_the_words_gets_it_and_the_link()
    {
        var at = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        LiliaMcpAccountTools.PlanLimitMessage(new QuotaExceededException("x", 1, 1, at, "This limit is reached."))
            .Should().Be($"This limit is reached. This limit renews on 1 December (UTC). Plans and prices: {Pricing}");
        LiliaMcpAccountTools.PlanLimitMessage(new QuotaExceededException("x", 1, 1, null, "Your plan holds 3 documents."))
            .Should().Be($"Your plan holds 3 documents. Plans and prices: {Pricing}");
        LiliaMcpAccountTools.MaskEmail("oussama@gmail.com").Should().Be("o•••@gmail.com");
        LiliaMcpAccountTools.MaskEmail("not-an-email").Should().BeNull();
        LiliaMcpAccountTools.DisplayName("someone@example.org").Should().BeNull("a name that is an email is never echoed");
    }
}
