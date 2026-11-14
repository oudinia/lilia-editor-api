using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// The owner's rule for unused Ask Lilia credits (14 Nov 2026, "Capped, 3 months"): they roll over, never more than
/// one month's allowance, expire after 3 months and are lost when the plan ends. Free's taste is 30 a month.
/// </summary>
public class AskRolloverRuleTests
{
    [Fact]
    public void A_month_left_unused_carries_its_whole_allowance()
        => AskRollover.Carry(30, [0]).Should().Be(30);

    [Fact]
    public void Only_what_was_left_carries()
        => AskRollover.Carry(30, [12]).Should().Be(18);

    [Fact]
    public void The_carry_never_passes_one_month_s_allowance()
        => AskRollover.Carry(500, [0, 0, 0]).Should().Be(500, "three quiet months still carry one month, not three");

    [Fact]
    public void Carried_credits_are_spent_first()
        // Month 1 has 30 + 30 carried; spending 40 uses the carried 30 first, so 20 of month 1's own are left.
        => AskRollover.Carry(30, [0, 40]).Should().Be(20);

    [Fact]
    public void A_month_that_spent_more_than_it_had_carries_nothing()
        => AskRollover.Carry(30, [0, 90]).Should().Be(0);

    [Fact]
    public void What_carries_is_always_last_month_s_own_credits()
        // Month 1 keeps its own 30 and the cap drops month 0's; month 2 spends the 30 it was carried, so the 30 it
        // carries on are its own: no credit reaches its third month.
        => AskRollover.Carry(30, [0, 0, 30]).Should().Be(30);

    [Fact]
    public void No_allowance_carries_nothing()
    {
        AskRollover.Carry(0, [0, 0]).Should().Be(0);
        AskRollover.Carry(-1, [0]).Should().Be(0, "-1 is unlimited and needs no carry");
    }
}

public class AskRolloverTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public AskRolloverTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private IEntitlementService Entitlements(out IServiceScope scope)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b => b.UseSetting("AI:ComingSoon", "false"));
        _factories.Add(factory);
        scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IEntitlementService>();
    }

    private static string NewId(string what) => $"roll_{what}_{Guid.NewGuid():N}"[..32];

    private static DateTime ThisMonth => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task SpendAsync(string userId, int credits, DateTime at)
    {
        await using var db = CreateDbContext();
        db.AiCreditLedger.Add(new AiCreditLedger
        {
            Id = Guid.NewGuid(), UserId = userId, Delta = -credits, Reason = "spend", Note = "test", CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private async Task JoinedAsync(string userId, DateTime at)
    {
        await using var db = CreateDbContext();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAt, at));
    }

    [Fact]
    public async Task A_new_account_has_nothing_carried()
    {
        var free = NewId("new");
        await SeedUserAsync(free);
        var plans = Entitlements(out var scope);
        using var _ = scope;

        (await Ask(plans, free, 31))!.Cap.Should().Be(30, "months before the account existed left nothing unused");
    }

    private static async Task<QuotaExceededException?> Ask(IEntitlementService plans, string userId, int credits)
    {
        try
        {
            await plans.EnsureQuotaAsync(userId, QuotaResource.AiCredits, credits);
            return null;
        }
        catch (QuotaExceededException q)
        {
            return q;
        }
    }

    [Fact]
    public async Task Free_has_a_taste_of_30_credits_and_Pro_500()
    {
        await using var db = CreateDbContext();
        var caps = await db.Plans.Where(p => p.Slug == "pro" || p.Slug == "free").ToDictionaryAsync(p => p.Slug, p => p.Caps);
        caps["free"].RootElement.GetProperty("aiCreditsPerMonth").GetInt32().Should().Be(30);
        caps["pro"].RootElement.GetProperty("aiCreditsPerMonth").GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task Last_month_s_unused_credits_are_added_to_this_month()
    {
        var free = NewId("free");
        await SeedUserAsync(free);
        await JoinedAsync(free, ThisMonth.AddMonths(-1));
        await SpendAsync(free, 10, ThisMonth.AddMonths(-1).AddDays(3));   // 20 of last month's 30 left
        await SpendAsync(free, 25, ThisMonth.AddHours(1));
        var plans = Entitlements(out var scope);
        using var _ = scope;

        (await Ask(plans, free, 25)).Should().BeNull("25 + 25 is within 30 + the 20 carried");
        var refused = await Ask(plans, free, 26);
        refused!.Resource.Should().Be("aiCreditsPerMonth");
        refused.Cap.Should().Be(50);
    }

    [Fact]
    public async Task A_plan_that_ended_loses_its_carried_credits()
    {
        var user = NewId("cancel");
        await SeedUserAsync(user);
        await JoinedAsync(user, ThisMonth.AddMonths(-4));
        await using (var db = CreateDbContext())
        {
            var pro = await db.Plans.SingleAsync(p => p.Slug == "pro");
            var ended = ThisMonth.AddHours(2);
            db.UserPlans.Add(new UserPlan
            {
                Id = Guid.NewGuid(), UserId = user, PlanId = pro.Id, Status = "cancelled",
                StartedAt = ThisMonth.AddMonths(-3), EndsAt = ended, UpdatedAt = ended,
            });
            await db.SaveChangesAsync();
        }
        var plans = Entitlements(out var scope);
        using var _ = scope;

        (await Ask(plans, user, 30)).Should().BeNull("Free's own 30");
        (await Ask(plans, user, 31))!.Cap.Should().Be(30, "Pro's unused months ended with Pro");
    }

    [Fact]
    public async Task The_plan_meter_counts_the_carried_credits()
    {
        var free = NewId("meter");
        await SeedUserAsync(free);
        await JoinedAsync(free, ThisMonth.AddMonths(-2));
        await SpendAsync(free, 30, ThisMonth.AddHours(1));   // last month unused: 30 carried, so half of 60 is used
        var plans = Entitlements(out var scope);
        using var _ = scope;

        var usage = await plans.GetUsageAsync(free);

        usage!.Ask.Fraction.Should().BeApproximately(0.5, 0.001);
    }
}
