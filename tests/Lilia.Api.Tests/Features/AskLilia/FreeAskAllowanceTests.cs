using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Free's Ask Lilia allowance is 75 credits a month (the owner, 27 Nov 2026), a hard stop when
/// <c>AI:EnforceCredits</c> is on, with unused credits still rolling over. The model is scripted.
/// An empty <c>AI:ComingSoonPlans</c> list, with <c>AI:ComingSoon</c> on, still means every plan.
/// </summary>
public class FreeAskAllowanceTests : IntegrationTestBase, IDisposable
{
    private const int FreeAllowance = 75;

    private readonly ScriptedChatClient _chat = new();
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public FreeAskAllowanceTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    /// <param name="comingSoonPlans">Plans <c>AI:ComingSoon</c> names. Null leaves the list unset.</param>
    /// <param name="emptyComingSoonPlans">Bind <c>AI:ComingSoonPlans</c> to a JSON empty array (the trap: that is every plan).</param>
    private HttpClient Caller(string userId, bool comingSoon, IReadOnlyList<string>? comingSoonPlans = null, bool emptyComingSoonPlans = false)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            if (comingSoon) b.UseSetting("AI:ComingSoon", "true");
            if (comingSoonPlans is not null)
            {
                for (var i = 0; i < comingSoonPlans.Count; i++)
                    b.UseSetting($"AI:ComingSoonPlans:{i}", comingSoonPlans[i]);
            }
            if (emptyComingSoonPlans)
            {
                // A JSON [] writes no keys, so the binder returns null. The service treats null and an empty
                // list the same: every plan is coming soon. Do not "fix" that by adding an indexed entry here.
                var json = """{"AI":{"ComingSoon":true,"ComingSoonPlans":[],"EnforceCredits":true}}""";
                var bytes = Encoding.UTF8.GetBytes(json);
                b.ConfigureAppConfiguration((_, config) => config.AddJsonStream(new MemoryStream(bytes)));
            }
            b.UseSetting("AI:Anthropic:ApiKey", "sk-test-not-a-key");
            b.UseSetting("AI:Enabled", "true");
            b.UseSetting("AI:EnforceCredits", "true");
            b.UseSetting("AI:MonthlyBudgetUsd", "0");
            b.UseSetting("AI:FreeMonthlyBudgetUsd", "0");
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

    private static string NewId(string what) => $"freeask_{what}_{Guid.NewGuid():N}"[..40];

    private static DateTime ThisMonth => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task SpendAsync(string userId, int credits, DateTime? at = null)
    {
        await using var db = CreateDbContext();
        db.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(), UserId = userId, Delta = -credits, Reason = "spend", Note = "seeded",
            CreatedAt = at ?? DateTime.UtcNow,
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

    private async Task JoinedAsync(string userId, DateTime at)
    {
        await using var db = CreateDbContext();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAt, at));
    }

    private static object Ask(string message) => new { message };

    [Fact]
    public async Task Free_reaches_the_model_when_coming_soon_names_another_plan_and_Pro_is_unaffected()
    {
        var free = NewId("free");
        var pro = NewId("pro");
        await SeedUserAsync(free);
        await SeedUserAsync(pro);
        await GivePlanAsync(free, "free");
        await GivePlanAsync(pro, "pro");
        _chat.Enqueue("It is.", "It is.");

        var freeClient = Caller(free, comingSoon: true, comingSoonPlans: ["student"]);
        var freeAsk = await freeClient.PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        freeAsk.StatusCode.Should().Be(HttpStatusCode.OK, await freeAsk.Content.ReadAsStringAsync());
        (await freeClient.GetFromJsonAsync<JsonElement>("/api/plan")).GetProperty("ask").GetProperty("comingSoon").GetBoolean()
            .Should().BeFalse();

        var proClient = Caller(pro, comingSoon: true, comingSoonPlans: ["student"]);
        var proAsk = await proClient.PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        proAsk.StatusCode.Should().Be(HttpStatusCode.OK, await proAsk.Content.ReadAsStringAsync());
        (await proClient.GetFromJsonAsync<JsonElement>("/api/plan")).GetProperty("ask").GetProperty("comingSoon").GetBoolean()
            .Should().BeFalse();

        _chat.Calls.Should().HaveCount(2, "Free and Pro both reach the model; student is the plan that waits");
    }

    [Fact]
    public async Task Free_stops_at_75_credits_and_the_next_ask_does_not_call_the_model()
    {
        var under = NewId("under");
        var spent = NewId("spent");
        await SeedUserAsync(under);
        await SeedUserAsync(spent);
        await GivePlanAsync(under, "free");
        await GivePlanAsync(spent, "free");
        await SpendAsync(under, FreeAllowance - 1);
        await SpendAsync(spent, FreeAllowance);
        _chat.Enqueue("Yes.");

        var open = await Caller(under, comingSoon: true, comingSoonPlans: ["student"]).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        open.StatusCode.Should().Be(HttpStatusCode.OK, await open.Content.ReadAsStringAsync());
        _chat.Calls.Should().ContainSingle("one credit under the allowance still reaches the model");

        var refused = await Caller(spent, comingSoon: true, comingSoonPlans: ["student"]).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("locked").GetBoolean().Should().BeTrue();
        json.GetProperty("reason").GetString().Should().Be("over-budget");
        json.GetProperty("message").GetString().Should().Be("You've used all your AI credits for this period.");
        _chat.Calls.Should().ContainSingle("the 75th credit already used is a hard stop: no further model call");
    }

    [Fact]
    public async Task Carried_credits_raise_the_hard_stop_to_75_plus_one_capped_month()
    {
        // Three quiet months still carry one allowance (AskRollover), so the stop is 75 + 75, not 75 + 225.
        const int carry = FreeAllowance;
        const int limit = FreeAllowance + carry;
        var joined = ThisMonth.AddMonths(-AskRollover.LookBackMonths);

        var under = NewId("rollunder");
        var spent = NewId("rollspent");
        await SeedUserAsync(under);
        await SeedUserAsync(spent);
        await JoinedAsync(under, joined);
        await JoinedAsync(spent, joined);
        await SpendAsync(under, limit - 1, ThisMonth.AddHours(1));
        await SpendAsync(spent, limit, ThisMonth.AddHours(1));
        _chat.Enqueue("Yes.");

        var open = await Caller(under, comingSoon: true, comingSoonPlans: ["student"]).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        open.StatusCode.Should().Be(HttpStatusCode.OK, await open.Content.ReadAsStringAsync());
        _chat.Calls.Should().ContainSingle("149 used is inside 75 plus the capped carry of 75");

        var refused = await Caller(spent, comingSoon: true, comingSoonPlans: ["student"]).PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()
            .Should().Be("You've used all your AI credits for this period.");
        _chat.Calls.Should().ContainSingle("the hard stop is 75 + the capped carry, and it does not call the model");
    }

    [Fact]
    public async Task An_empty_coming_soon_list_keeps_every_plan_coming_soon()
    {
        var free = NewId("soonfree");
        var pro = NewId("soonpro");
        await SeedUserAsync(free);
        await SeedUserAsync(pro);
        await GivePlanAsync(free, "free");
        await GivePlanAsync(pro, "pro");

        var freeClient = Caller(free, comingSoon: true, emptyComingSoonPlans: true);
        var bound = _factories[^1].Services.GetRequiredService<IConfiguration>().GetSection("AI:ComingSoonPlans").Get<string[]>();
        (bound is null || bound.Length == 0).Should().BeTrue("an empty AI:ComingSoonPlans list binds as no named plans");

        var freeAsk = await freeClient.PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        (await freeAsk.Content.ReadAsStringAsync()).Should().Contain("coming soon");
        (await freeClient.GetFromJsonAsync<JsonElement>("/api/plan")).GetProperty("ask").GetProperty("comingSoon").GetBoolean()
            .Should().BeTrue();

        var proClient = Caller(pro, comingSoon: true, emptyComingSoonPlans: true);
        var proAsk = await proClient.PostAsJsonAsync("/api/ai/ask", Ask("Is this clear?"));
        (await proAsk.Content.ReadAsStringAsync()).Should().Contain("coming soon");
        (await proClient.GetFromJsonAsync<JsonElement>("/api/plan")).GetProperty("ask").GetProperty("comingSoon").GetBoolean()
            .Should().BeTrue();

        _chat.Calls.Should().BeEmpty("an empty list does not mean no plan waits; it means every plan waits");
    }
}
