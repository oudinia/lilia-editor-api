using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Accounts;

/// <summary>
/// The companion-app waitlist had no test (2 Nov 2026, the coverage round). Anonymous by design: anyone
/// can sign up, once per address, and the count is public. A locale or source longer than its column
/// failed the save, and the failure was answered "already subscribed" without saving anything.
/// </summary>
public class WaitlistControllerTests : IntegrationTestBase
{
    public WaitlistControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string Url = "/api/waitlist/companion-app";

    [Fact]
    public async Task Anyone_signs_up_once_per_address_and_the_count_is_public()
    {
        using var anon = CreateAnonymousClient();
        // The waitlist isn't cleared between tests: counted before and after.
        var before = (await anon.GetFromJsonAsync<Dictionary<string, int>>($"{Url}/count"))!["count"];
        var first = await anon.PostAsJsonAsync(Url, new { email = "Reader@Example.org", locale = "FR", source = "banner" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadAsStringAsync()).Should().Contain("\"added\":true");

        // The same address in other letters: already on the list, still a success.
        var again = await anon.PostAsJsonAsync(Url, new { email = "reader@example.org" });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadAsStringAsync()).Should().Contain("already_subscribed");

        var count = await anon.GetFromJsonAsync<Dictionary<string, int>>($"{Url}/count");
        count!["count"].Should().Be(before + 1);

        await using var db = CreateDbContext();
        var row = await db.CompanionAppWaitlist.SingleAsync(w => w.Email == "Reader@Example.org");
        row.Email.Should().Be("Reader@Example.org");
        row.Locale.Should().Be("fr");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task A_missing_or_malformed_address_is_refused(string email)
    {
        using var anon = CreateAnonymousClient();
        var r = await anon.PostAsJsonAsync(Url, new { email });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("invalid_email");
    }

    [Fact]
    public async Task A_long_locale_or_source_is_cut_to_fit_and_the_signup_is_saved()
    {
        using var anon = CreateAnonymousClient();
        var r = await anon.PostAsJsonAsync(Url, new { email = "long@example.org", locale = "zh-Hant-TW-x-private", source = new string('s', 80) });
        (await r.Content.ReadAsStringAsync()).Should().Contain("\"added\":true");
        await using var db = CreateDbContext();
        var row = await db.CompanionAppWaitlist.SingleAsync(w => w.Email == "long@example.org");
        row.Locale.Should().HaveLength(10);
        row.Source.Should().HaveLength(40);
    }
}
