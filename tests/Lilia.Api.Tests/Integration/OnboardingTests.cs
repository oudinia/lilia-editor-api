using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Features.Onboarding;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Integration.Sharing;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Integration;

/// <summary>
/// Welcome and onboarding (reports/2026-10-21-welcome-onboarding.md), build A: a new account gets a
/// quiet default team and one welcome email in its language, once its address is real; the web
/// records its first steps. Accounts are created through IUserService, as the sign-in sync does
/// (the sync middleware itself doesn't run under the test sign-in).
/// </summary>
public class OnboardingTests : IntegrationTestBase
{
    private readonly RecordingEmailService _mail = new();
    private readonly FakeStytch _stytch = new();
    private readonly WebApplicationFactory<Program> _factory;

    public OnboardingTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailService>();
            s.AddSingleton<IEmailService>(_mail);
            s.RemoveAll<IStytchUserLookup>();
            s.AddSingleton<IStytchUserLookup>(_stytch);
            // The shared factory stubs the user service; these tests need the real one, which
            // publishes UserCreatedEvent.
            s.RemoveAll<IUserService>();
            s.AddScoped<IUserService, UserService>();
        }));
    }

    private sealed class FakeStytch : IStytchUserLookup
    {
        public bool Asked { get; set; }
        public Dictionary<string, StytchUserInfo> Users { get; } = new();
        public bool CanAsk { get; set; } = true;
        public Task<StytchUserInfo?> FindAsync(string userId, CancellationToken ct = default)
        {
            Asked = true;
            return Task.FromResult(Users.GetValueOrDefault(userId));
        }
    }

    private async Task SignUpAsync(string id, string email, string? name, string? locale)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUserService>()
            .CreateOrUpdateUserAsync(new CreateOrUpdateUserDto(id, email, name, null, locale));
    }

    /// <summary>Signed in as <paramref name="id"/>, with the account's own address (the sign-in sync keeps the token's).</summary>
    private HttpClient As(string id, string? locale = null, string? name = null, string? email = null)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", id);
        c.DefaultRequestHeaders.Add("X-Test-Email", email ?? $"{id}@lilia.test");
        if (name is not null) c.DefaultRequestHeaders.Add("X-Test-Name", name);
        if (locale is not null) c.DefaultRequestHeaders.Add("X-Lilia-Locale", locale);
        return c;
    }

    private async Task<IReadOnlyList<RecordingEmailService.Sent>> MailTo(string email, int atLeast)
    {
        // The welcome goes out from a message handler, a moment after the account is created.
        for (var i = 0; i < 50 && _mail.To(email).Count < atLeast; i++) await Task.Delay(100);
        await Task.Delay(400); // and nothing more after it
        return _mail.To(email);
    }

    private async Task<Lilia.Core.Entities.User> UserAsync(string id)
    {
        await using var db = CreateDbContext();
        return await db.Users.AsNoTracking().FirstAsync(u => u.Id == id);
    }

    [Fact]
    public async Task A_new_Google_account_gets_one_welcome_in_its_language_and_a_quiet_team()
    {
        _stytch.Users["user-test-nadia"] = new StytchUserInfo("nadia@lilia.test", "Nadia Benali", EmailVerified: true);

        await SignUpAsync("user-test-nadia", "nadia@lilia.test", "Nadia Benali", "fr");

        var mail = await MailTo("nadia@lilia.test", 1);
        mail.Should().ContainSingle(m => m.Kind == "welcome").Which.Detail.Should().Be("fr");
        mail.Should().NotContain(m => m.Kind == "team-welcome", "the default team is quiet");
        for (var i = 0; i < 30 && (await UserAsync("user-test-nadia")).DefaultTeamId is null; i++) await Task.Delay(100);
        (await UserAsync("user-test-nadia")).DefaultTeamId.Should().NotBeNull();

        // Signing in again, or the web asking, never sends a second one.
        await SignUpAsync("user-test-nadia", "nadia@lilia.test", "Nadia Benali", "fr");
        var dto = await As("user-test-nadia", email: "nadia@lilia.test").GetFromJsonAsync<OnboardingDto>("/api/me/onboarding");
        dto!.WelcomeSent.Should().BeTrue();
        dto.Verified.Should().BeTrue();
        dto.Legacy.Should().BeFalse();
        (await MailTo("nadia@lilia.test", 2)).Count(m => m.Kind == "welcome").Should().Be(1);
    }

    [Fact]
    public async Task An_email_sign_up_gets_the_welcome_only_after_verifying()
    {
        _stytch.Users["user-test-omar"] = new StytchUserInfo("omar@lilia.test", "Omar", EmailVerified: false);
        await SignUpAsync("user-test-omar", "omar@lilia.test", "Omar", "es");
        (await MailTo("omar@lilia.test", 1)).Should().BeEmpty("the address isn't verified yet");

        _stytch.Users["user-test-omar"] = new StytchUserInfo("omar@lilia.test", "Omar", EmailVerified: true);
        var res = await As("user-test-omar", email: "omar@lilia.test").PostAsync("/api/me/onboarding/verified", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<OnboardingDto>())!.WelcomeSent.Should().BeTrue();
        (await MailTo("omar@lilia.test", 1)).Should().ContainSingle(m => m.Kind == "welcome").Which.Detail.Should().Be("es");
    }

    [Fact]
    public async Task A_stand_in_address_never_gets_a_welcome()
    {
        _stytch.CanAsk = false;
        await SignUpAsync("user-test-ghost", "user-test-ghost@auth.liliaeditor.com", null, null);
        (await MailTo("user-test-ghost@auth.liliaeditor.com", 1)).Should().BeEmpty();
        (await As("user-test-ghost", email: "user-test-ghost@auth.liliaeditor.com").GetFromJsonAsync<OnboardingDto>("/api/me/onboarding"))!.WelcomeSent.Should().BeFalse();
    }

    [Fact]
    public async Task An_account_from_before_onboarding_gets_no_welcome_and_no_forced_tour()
    {
        await SeedUserAsync("veteran", "veteran@lilia.test", "Vera");
        await using (var db = CreateDbContext())
            await db.Users.Where(u => u.Id == "veteran")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

        var dto = await As("veteran", email: "veteran@lilia.test").GetFromJsonAsync<OnboardingDto>("/api/me/onboarding");
        dto!.Legacy.Should().BeTrue();
        dto.TourSeen.Should().BeTrue();
        (await MailTo("veteran@lilia.test", 1)).Should().BeEmpty();
    }

    [Fact]
    public async Task Steps_are_recorded_once_and_say_when_they_are_new()
    {
        await SeedUserAsync("stepper", "stepper@lilia.test", "Sam");
        var c = As("stepper", "fr-FR", name: "Sam", email: "stepper@lilia.test");

        async Task<OnboardingDto> Post(string step, object? body = null)
        {
            var res = body is null ? await c.PostAsync($"/api/me/onboarding/{step}", null) : await c.PostAsJsonAsync($"/api/me/onboarding/{step}", body);
            res.StatusCode.Should().Be(HttpStatusCode.OK, step);
            return (await res.Content.ReadFromJsonAsync<OnboardingDto>())!;
        }

        (await Post("first-document", new { route = "template" })).Should().Match<OnboardingDto>(d => d.JustRecorded && d.FirstDocumentRoute == "template");
        (await Post("first-document", new { route = "blank" })).Should().Match<OnboardingDto>(d => !d.JustRecorded && d.FirstDocumentRoute == "template");
        (await Post("first-validate-pass")).JustRecorded.Should().BeTrue();
        (await Post("first-validate-pass")).JustRecorded.Should().BeFalse();
        (await Post("first-export")).JustRecorded.Should().BeTrue();
        await Post("tour-step", new { step = 3 });
        (await Post("tour-step", new { step = 2 })).TourStepReached.Should().Be(3, "the furthest step is kept");
        (await Post("tour-seen")).TourSeen.Should().BeTrue();
        (await Post("tour-restart")).TourSeen.Should().BeTrue("a restart from Help doesn't bring the tour back on its own");
        (await Post("tips-opt-out")).TipsOptOut.Should().BeTrue();
        await Post("start", new { name = "Ignored", timeZone = "Europe/Paris" });

        (await c.PostAsync("/api/me/onboarding/anything", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var row = await db.UserOnboarding.AsNoTracking().FirstAsync(o => o.UserId == "stepper");
        row.TourRestarts.Should().Be(1);
        row.TimeZone.Should().Be("Europe/Paris");
        (await UserAsync("stepper")).Name.Should().Be("Sam", "a name the account already has is kept");
        (await UserAsync("stepper")).Locale.Should().Be("fr", "the interface language follows the web");
    }

    [Fact]
    public async Task Sign_in_emails_are_limited_per_address()
    {
        // No Stytch secret here: an allowed request goes as far as "service_unavailable"; one over
        // the limit stops before, with the same plausible answer and no email.
        var anon = CreateAnonymousClient();
        async Task<string> Ask(string email) =>
            await (await anon.PostAsJsonAsync("/api/auth/resend-verification", new { email, locale = "fr" })).Content.ReadAsStringAsync();

        (await Ask("flood@lilia.test")).Should().Contain("service_unavailable");
        (await Ask("FLOOD@lilia.test")).Should().Be("{\"sent\":true}", "a second one within the minute is held back");
        (await Ask("other@lilia.test")).Should().Contain("service_unavailable", "another address has its own limit");
    }

    [Fact]
    public async Task Onboarding_needs_a_signed_in_person()
    {
        var res = await CreateAnonymousClient().GetAsync("/api/me/onboarding");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
