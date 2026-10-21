using System.Net;
using FluentAssertions;
using Lilia.Api.Features.Onboarding;
using Lilia.Api.Services;
using Lilia.Api.Services.Email;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Integration.Sharing;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Integration;

/// <summary>
/// The day-3 follow-up (owner's yes, 21 Oct 2026): once, at 10:00 local, only to someone who confirmed
/// their address and hasn't started; and "Don't send me tips" works in one click.
/// </summary>
public class OnboardingFollowUpTests : IntegrationTestBase
{
    public OnboardingFollowUpTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>11 Nov 2026, 09:30 UTC = 10:30 in Paris.</summary>
    private static readonly DateTime Now = new(2026, 11, 11, 9, 30, 0, DateTimeKind.Utc);

    private sealed class At(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private readonly RecordingEmailService _mail = new();

    private async Task<int> RunAsync(DateTime? now = null)
    {
        await using var db = CreateDbContext();
        var followUp = new OnboardingFollowUp(db, _mail, new At(now ?? Now), NullLogger<OnboardingFollowUp>.Instance,
            new EmailSettings { BaseUrl = "https://editor.liliaeditor.com" });
        return await followUp.SendDueAsync();
    }

    private async Task<string> PersonAsync(string id, Action<UserOnboarding>? shape = null, double daysAgo = 4)
    {
        var created = Now.AddDays(-daysAgo);
        await SeedUserAsync(id, $"{id}@lilia.test", "Nadia Benali");
        await using var db = CreateDbContext();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, created).SetProperty(u => u.Locale, "fr"));
        var row = new UserOnboarding { UserId = id, CreatedAt = created, VerifiedAt = created, TimeZone = "Europe/Paris" };
        shape?.Invoke(row);
        db.UserOnboarding.Add(row);
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Someone_who_has_not_started_gets_it_once_at_ten_their_time()
    {
        await PersonAsync("fu-due");

        (await RunAsync()).Should().Be(1);
        var sent = _mail.To("fu-due@lilia.test").Should().ContainSingle(m => m.Kind == "getting-started").Subject;
        sent.Detail.Should().StartWith("https://editor.liliaeditor.com/api/onboarding/tips-off?t=");

        (await RunAsync()).Should().Be(0, "it is sent once");
        (await RunAsync(Now.AddHours(1))).Should().Be(0);
    }

    [Fact]
    public async Task Nobody_else_gets_it()
    {
        await PersonAsync("fu-optout", o => o.TipsOptOut = true);
        await PersonAsync("fu-started", o => o.FirstDocumentAt = Now.AddDays(-3.5));
        await PersonAsync("fu-unconfirmed", o => o.VerifiedAt = null);
        await PersonAsync("fu-early", daysAgo: 2);
        await PersonAsync("fu-late", daysAgo: 11);
        await PersonAsync("fu-tokyo", o => o.TimeZone = "Asia/Tokyo");
        var owner = await PersonAsync("fu-owner");
        await SeedDocumentAsync(owner, "Mine");
        var invited = await PersonAsync("fu-invited");
        var other = await PersonAsync("fu-other", o => o.FirstDocumentAt = Now);
        var shared = await SeedDocumentAsync(other, "Shared");
        await using (var db = CreateDbContext())
        {
            db.DocumentCollaborators.Add(new DocumentCollaborator { Id = Guid.NewGuid(), DocumentId = shared.Id, UserId = invited, RoleId = Guid.Parse("00000000-0000-0000-0000-000000000003"), CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        (await RunAsync()).Should().Be(0);
        _mail.Outbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Dont_send_me_tips_works_in_one_click_and_says_so()
    {
        await PersonAsync("fu-click");
        await RunAsync();
        var url = _mail.To("fu-click@lilia.test").Single().Detail;

        var res = await CreateAnonymousClient().GetAsync(new Uri(url).PathAndQuery);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("data-testid=\"tips-off\"").And.Contain("Plus de conseils");
        await using (var db = CreateDbContext())
            (await db.UserOnboarding.AsNoTracking().SingleAsync(o => o.UserId == "fu-click")).TipsOptOut.Should().BeTrue();

        // Mail apps' one-click unsubscribe, and an unknown token: the same calm answer.
        (await CreateAnonymousClient().PostAsync(new Uri(url).PathAndQuery, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateAnonymousClient().GetAsync("/api/onboarding/tips-off?t=nope")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void The_email_reads_well_with_or_without_a_name_and_never_offers_slides()
    {
        foreach (var lang in EmailTemplates.Languages)
        {
            var named = EmailTemplates.Render("getting_started", lang, new Dictionary<string, string?>
            {
                ["action_url"] = "https://editor.liliaeditor.com/", ["recipient_email"] = "n@x.org", ["first_name"] = "Nadia",
                ["tips_url"] = "https://editor.liliaeditor.com/api/onboarding/tips-off?t=abc",
            });
            named.Text.Should().Contain("Nadia").And.Contain("tips-off?t=abc").And.NotContain("{{");
            named.Text.Should().NotContainAny("slides", "diapositiv");
        }
        var nameless = EmailTemplates.Render("getting_started", "en", new Dictionary<string, string?> { ["tips_url"] = "u", ["action_url"] = "a" });
        nameless.Text.Should().Contain("Hi, you haven");
        EmailTemplates.Render("getting_started", "es", new Dictionary<string, string?> { ["tips_url"] = "u", ["action_url"] = "a" })
            .Text.Should().Contain("Hola: todavía");
    }

    [Theory]
    [InlineData("Europe/Paris", 10), InlineData("Asia/Tokyo", 18), InlineData("Not/AZone", 10), InlineData(null, 10)]
    public void Local_time_uses_the_browser_zone_else_Paris(string? zone, int hour) =>
        OnboardingFollowUp.LocalTime(Now, zone).Hour.Should().Be(hour);
}
