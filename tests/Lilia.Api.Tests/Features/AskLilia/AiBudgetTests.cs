using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// The site's AI budget (4 Nov 2026). Lilia launches on a fixed, prepaid amount of AI, so the month's spend
/// for everyone stops at AI:MonthlyBudgetUsd and free accounts' share at AI:FreeMonthlyBudgetUsd; the
/// editor's quick AI actions are checked and counted like Ask Lilia; and Pro Plus unlocks what Pro does.
/// Each test seeds its own accounts: the spend of other tests' accounts stays out of the free pool's sum,
/// and the whole-site cases use budgets far from anything other tests spend. The model is scripted.
/// </summary>
public class AiBudgetTests : IntegrationTestBase, IDisposable
{
    private readonly ScriptedChatClient _chat = new();
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public AiBudgetTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private HttpClient Caller(string userId, decimal siteBudget = 0m, decimal freeBudget = 0m, bool comingSoon = false)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            if (comingSoon) b.UseSetting("AI:ComingSoon", "true");
            b.UseSetting("AI:Anthropic:ApiKey", "sk-test-not-a-key");
            b.UseSetting("AI:Enabled", "true");
            b.UseSetting("AI:EnforceCredits", "true");
            b.UseSetting("AI:MonthlyBudgetUsd", siteBudget.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("AI:FreeMonthlyBudgetUsd", freeBudget.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IChatClient)).ToList()) s.Remove(d);
                s.AddSingleton<IChatClient>(_chat);
            });
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static string NewId(string what) => $"budget_{what}_{Guid.NewGuid():N}"[..40];

    /// <summary>A spend row this month, as a model call would leave it.</summary>
    private async Task SpendAsync(string userId, int credits)
    {
        await using var db = CreateDbContext();
        db.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(), UserId = userId, Delta = -credits, Reason = "spend", Note = "seeded", CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task GivePlanAsync(string userId, string slug)
    {
        await using var db = CreateDbContext();
        var plan = await db.Plans.SingleAsync(p => p.Slug == slug);
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static object Ask(string message) => new { message };

    [Fact]
    public async Task When_the_month_s_AI_is_used_up_Ask_Lilia_rests_for_everyone()
    {
        var spender = NewId("spender");
        var author = NewId("author");
        await SeedUserAsync(spender);
        await SeedUserAsync(author);
        await GivePlanAsync(author, "pro");
        // $0.06 of budget; the spender's 50 credits are $0.30, over it whatever else this month holds.
        await SpendAsync(spender, 50);

        var res = await Caller(author, siteBudget: 0.06m).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("resting").And.Contain("Everything else works as usual");
        _chat.Calls.Should().BeEmpty("the budget is checked before the model is called");
    }

    // The connector-first launch (10 Nov 2026): with AI:ComingSoon, Ask Lilia says "coming soon" and calls no model, the
    // editor's quick AI actions are refused with their own code, and the plan tells the panel so.
    [Fact]
    public async Task Coming_soon_Ask_Lilia_calls_no_model_and_says_so()
    {
        var author = NewId("soon");
        await SeedUserAsync(author);
        await GivePlanAsync(author, "pro");
        var client = Caller(author, comingSoon: true);

        var ask = await client.PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        (await ask.Content.ReadAsStringAsync()).Should().Contain("coming soon").And.Contain("connect Claude");

        var quick = await client.PostAsJsonAsync("/api/ai/improve-text", new { text = "A sentence.", action = "improve" });
        quick.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await quick.Content.ReadAsStringAsync()).Should().Contain("AI_COMING_SOON");

        var plan = await client.GetFromJsonAsync<JsonElement>("/api/plan");
        plan.GetProperty("ask").GetProperty("comingSoon").GetBoolean().Should().BeTrue();
        _chat.Calls.Should().BeEmpty("nothing reaches the model while Ask Lilia is coming soon");
    }

    [Fact]
    public async Task Under_the_budget_Ask_Lilia_answers()
    {
        var author = NewId("author");
        await SeedUserAsync(author);
        _chat.Enqueue("It is.");

        var res = await Caller(author, siteBudget: 1_000_000m, freeBudget: 1_000_000m).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        _chat.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Free_accounts_stop_at_their_share_and_paying_authors_go_on()
    {
        var freeSpender = NewId("free");
        var freeAuthor = NewId("free2");
        var proAuthor = NewId("pro");
        foreach (var u in new[] { freeSpender, freeAuthor, proAuthor }) await SeedUserAsync(u);
        await GivePlanAsync(proAuthor, "pro");
        await SpendAsync(freeSpender, 50);   // $0.30 of free use; the share below is $0.06
        _chat.Enqueue("Yes.");

        var free = await Caller(freeAuthor, freeBudget: 0.06m).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        (await free.Content.ReadAsStringAsync()).Should().Contain("free plan's AI is used up");
        _chat.Calls.Should().BeEmpty();

        var pro = await Caller(proAuthor, freeBudget: 0.06m).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        pro.StatusCode.Should().Be(HttpStatusCode.OK, await pro.Content.ReadAsStringAsync());
        _chat.Calls.Should().ContainSingle("a paying author's use doesn't come from the free share");
    }

    [Fact]
    public async Task The_editor_s_quick_AI_actions_are_counted()
    {
        var author = NewId("author");
        await SeedUserAsync(author);
        _chat.Usage = new UsageDetails { InputTokenCount = 2_000, OutputTokenCount = 500 };
        _chat.Enqueue("""{"improvedText":"A clearer sentence."}""");

        var res = await Caller(author).PostAsJsonAsync("/api/ai/improve-text", new { text = "A sentence that rambles.", action = "improve" });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        await using var db = CreateDbContext();
        var row = await db.AiCreditLedger.AsNoTracking().SingleAsync(l => l.UserId == author);
        row.Delta.Should().BeNegative();
        row.AiRequestId.Should().BeNull();
        row.Note.Should().Contain("2000+500");
    }

    [Fact]
    public async Task The_editor_s_quick_AI_actions_stop_with_the_budget()
    {
        var spender = NewId("spender");
        var author = NewId("author");
        await SeedUserAsync(spender);
        await SeedUserAsync(author);
        await SpendAsync(spender, 50);

        var res = await Caller(author, siteBudget: 0.06m).PostAsJsonAsync("/api/ai/improve-text", new { text = "A sentence.", action = "improve" });

        res.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await res.Content.ReadAsStringAsync()).Should().Contain("resting");
        _chat.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Pro_has_500_credits_and_Pro_Plus_1500_with_Pro_s_models()
    {
        await using var db = CreateDbContext();
        var plans = await db.Plans.AsNoTracking().Where(p => p.Slug == "pro" || p.Slug == "pro_plus").ToDictionaryAsync(p => p.Slug);

        Credits(plans["pro"]).Should().Be(500);
        plans["pro"].MonthlyPrice.Should().Be(10.99m);
        plans["pro"].YearlyPrice.Should().Be(99m);
        Credits(plans["pro_plus"]).Should().Be(1500);
        plans["pro_plus"].MonthlyPrice.Should().Be(19.99m);
        // Hidden (not listed, not sold) while Ask Lilia is "coming soon": the connector-first decision, 10 Nov 2026.
        plans["pro_plus"].IsActive.Should().BeFalse();
        Plan.ModelTier("pro_plus").Should().Be("pro");
        Plan.ModelTier("beta").Should().Be("free");

        static int Credits(Plan p) => p.Caps.RootElement.GetProperty("aiCreditsPerMonth").GetInt32();
    }
}
