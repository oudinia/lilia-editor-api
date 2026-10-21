using FluentAssertions;
using Lilia.Admin.Data;
using Lilia.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>The Stats page's numbers: who counts as active, the funnel, cohorts and percentiles.</summary>
[Collection("Admin")]
public class AdminStatsTests(AdminFixture fx)
{
    private static string NewId(string tag) => $"st-{tag}-{Guid.NewGuid():N}"[..28];

    [Fact]
    public async Task An_account_that_only_asked_Lilia_today_counts_as_active_and_moves_through_the_funnel()
    {
        var asker = NewId("ask");
        var exporter = NewId("exp");
        var now = DateTime.UtcNow;
        await using (var db = fx.CreateDbContext())
        {
            foreach (var id in new[] { asker, exporter })
                db.Users.Add(new User { Id = id, Email = $"{id}@lilia.test", CreatedAt = now, UpdatedAt = now });
            db.AiRequests.Add(new AiRequest
            {
                Id = Guid.NewGuid(), UserId = asker, Purpose = "rephrase", Provider = "anthropic", Model = "test-model",
                PromptHash = "x", PromptTokens = 100, CompletionTokens = 50, Status = "success", LatencyMs = 1200, CreatedAt = now,
            });
            db.Documents.Add(new Document { Id = Guid.NewGuid(), OwnerId = exporter, Title = "Exported", CreatedAt = now, UpdatedAt = now });
            db.Jobs.Add(new Job { Id = Guid.NewGuid(), TenantId = exporter, UserId = exporter, JobType = "EXPORT", Status = "completed", TargetFormat = "pdf", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        using var scope = fx.Factory.Services.CreateScope();
        var s = await scope.ServiceProvider.GetRequiredService<AdminStats>().StatsAsync(7);

        s.Dau.Should().BeGreaterThanOrEqualTo(2, "a request to Lilia and a new document both make an account active today");
        s.Daily.Single(x => x.Name == "Active accounts").Values[^1].Should().BeGreaterThanOrEqualTo(2);
        s.Funnel[0].Count.Should().BeGreaterThanOrEqualTo(2);
        s.Funnel.Single(f => f.Name == "Asked Lilia").Count.Should().BeGreaterThanOrEqualTo(1);
        s.Funnel.Single(f => f.Name == "Exported").Count.Should().BeGreaterThanOrEqualTo(1);
        s.AskByModel.Should().Contain(m => m.Name == "test-model" && m.Extra >= 150);
        s.Exports.Should().Contain(e => e.Name == "pdf");
        s.AskMs.Samples.Should().BeGreaterThanOrEqualTo(1);
        s.DayAxis.Should().HaveCount(7);
        s.Daily.Should().OnlyContain(series => series.Values.Count == 7);
    }

    [Fact]
    public async Task The_funnel_counts_the_onboarding_steps_and_how_first_documents_started()
    {
        var id = NewId("onb");
        var now = DateTime.UtcNow;
        await using (var db = fx.CreateDbContext())
        {
            db.Users.Add(new User { Id = id, Email = $"{id}@lilia.test", CreatedAt = now, UpdatedAt = now });
            db.UserOnboarding.Add(new UserOnboarding
            {
                UserId = id, CreatedAt = now, VerifiedAt = now, FirstDocumentAt = now, FirstDocumentRoute = "template",
                TourSeenAt = now, FirstValidatePassAt = now, FirstExportAt = now,
            });
            await db.SaveChangesAsync();
        }

        using var scope = fx.Factory.Services.CreateScope();
        var s = await scope.ServiceProvider.GetRequiredService<AdminStats>().StatsAsync(7);

        foreach (var step in new[] { "Confirmed their address", "Finished or skipped the tour", "First clean check", "Exported" })
            s.Funnel.Single(f => f.Name == step).Count.Should().BeGreaterThanOrEqualTo(1, step);
        s.FirstRoutes.Should().Contain(r => r.Name == "template" && r.Count >= 1);
    }

    [Fact]
    public void Cohorts_give_each_signup_week_its_share_active_in_the_weeks_after()
    {
        var today = new DateTime(2026, 10, 16);                 // a Friday
        var week = AdminStats.StartOfWeek(today).AddDays(-14);  // two weeks before this week's Monday
        var users = new List<(string, DateTime)> { ("a", week.AddDays(1)), ("b", week.AddDays(2)) };
        var activity = new List<AdminStats.Act>
        {
            new("a", week.AddDays(1)), new("b", week.AddDays(2)),   // both active their first week
            new("a", week.AddDays(8)),                               // only a the week after
        };
        var cohort = AdminStats.Cohorts(users, activity, today).Single(c => c.Week == DateOnly.FromDateTime(week));
        cohort.Size.Should().Be(2);
        cohort.ActivePercentByWeek.Take(3).Should().Equal(100, 50, 0);
        cohort.ActivePercentByWeek[3].Should().BeNull("that week hasn't happened yet");
    }

    [Fact]
    public void Percentiles_are_nearest_rank_and_empty_is_zero()
    {
        AdminStats.Pct([]).Should().Be(new Percentiles(0, 0, 0));
        AdminStats.Pct(Enumerable.Range(1, 100).ToList()).Should().Be(new Percentiles(100, 50, 95));
        AdminStats.Pct([7]).Should().Be(new Percentiles(1, 7, 7));
    }

    [Fact]
    public void A_week_starts_on_Monday()
    {
        AdminStats.StartOfWeek(new DateTime(2026, 10, 16)).Should().Be(new DateTime(2026, 10, 12));
        AdminStats.StartOfWeek(new DateTime(2026, 10, 18)).Should().Be(new DateTime(2026, 10, 12), "Sunday belongs to the week before");
        AdminStats.StartOfWeek(new DateTime(2026, 10, 12)).Should().Be(new DateTime(2026, 10, 12));
    }
}
