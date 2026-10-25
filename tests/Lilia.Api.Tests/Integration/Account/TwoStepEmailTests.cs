using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Integration.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// Two-step by email and trusted devices (25 Oct 2026). The owner, 24 Oct, without their phone: "the
/// login cannot be just through the authenticator app, the choice of receiving code by email is
/// important. Also, if the user trusts his laptop or tablet, he should be able to only have to login."
/// </summary>
[Collection("Integration")]
public class TwoStepEmailTests : IntegrationTestBase, IDisposable
{
    private readonly RecordingEmailService _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public TwoStepEmailTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailService>();
            s.AddSingleton<IEmailService>(_mail);
        }));
    }

    // xUnit makes a new instance per test: without this, each test's server keeps its database
    // connections for the rest of the run ("53300: too many clients" in later classes).
    public void Dispose() => _factory.Dispose();

    private static string Session(string id, params (string Type, DateTime At)[] factors) => JsonSerializer.Serialize(new
    {
        id,
        authentication_factors = factors.Select(f => new { type = f.Type, last_authenticated_at = f.At.ToString("O") }),
    });

    /// <summary>A browser signed in with a password only (a new Stytch session each time).</summary>
    private HttpClient PasswordOnly(string userId, string? cookie = null)
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-StytchSession", Session($"session-{Guid.NewGuid():N}", ("password", DateTime.UtcNow)));
        if (cookie is not null) c.DefaultRequestHeaders.Add("Cookie", cookie);
        return c;
    }

    private async Task<string> Author(bool twoStepOn = true)
    {
        var id = $"tse-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test");
        if (twoStepOn)
        {
            await using var db = CreateDbContext();
            var u = await db.Users.FirstAsync(x => x.Id == id);
            u.TwoStepEnabled = true;
            await db.SaveChangesAsync();
        }
        return id;
    }

    private string LastCode(string id) =>
        _mail.To($"{id}@lilia.test").Last(m => m.Kind == "two-step-code").Detail;

    [Fact]
    public async Task An_email_code_lets_this_session_in_and_only_this_one()
    {
        var id = await Author();
        var browser = PasswordOnly(id);
        (await browser.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sent = await browser.PostAsync("/api/me/two-step/email/send", null);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, "asking for a code is open before the second step");
        (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sentTo").GetString().Should().Be($"{id[0]}•••@lilia.test");
        var code = LastCode(id);
        code.Should().MatchRegex("^[0-9]{6}$");

        var wrong = code == "000000" ? "111111" : "000000";
        var bad = await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code = wrong });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("WRONG");

        (await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await browser.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PasswordOnly(id).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "another session still needs its own code");
        (await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a code works once");
    }

    [Fact]
    public async Task Five_wrong_tries_spend_the_code()
    {
        var id = await Author();
        var browser = PasswordOnly(id);
        await browser.PostAsync("/api/me/two-step/email/send", null);
        var code = LastCode(id);
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < TwoStepService.MaxAttempts; i++)
            await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code = wrong });
        var late = await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code });
        late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await late.Content.ReadAsStringAsync()).Should().Contain("TOO_MANY");
        (await browser.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Two_step_can_be_turned_on_with_an_email_code_alone()
    {
        var id = await Author(twoStepOn: false);
        var browser = PasswordOnly(id);
        (await browser.PutAsJsonAsync("/api/me/two-step", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "it needs a code first");
        await browser.PostAsync("/api/me/two-step/email/send", null);
        (await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code = LastCode(id) })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await browser.PutAsJsonAsync("/api/me/two-step", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PasswordOnly(id).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "two-step is now on");
    }

    [Fact]
    public async Task A_trusted_device_needs_only_the_password_until_it_is_forgotten()
    {
        var id = await Author();
        var browser = PasswordOnly(id);
        await browser.PostAsync("/api/me/two-step/email/send", null);
        var verified = await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code = LastCode(id), trustDevice = true });
        verified.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var setCookie = verified.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(TwoStepService.DeviceCookie + "="));
        setCookie.Should().Contain("httponly").And.Contain("secure").And.Contain("path=/api");
        var cookie = setCookie.Split(';')[0];

        var nextWeek = PasswordOnly(id, cookie);
        (await nextWeek.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK, "the password is enough on a trusted device");
        var devices = await nextWeek.GetFromJsonAsync<JsonElement>("/api/me/trusted-devices");
        devices.GetArrayLength().Should().Be(1);
        devices[0].GetProperty("current").GetBoolean().Should().BeTrue();

        (await nextWeek.DeleteAsync("/api/me/trusted-devices")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PasswordOnly(id, cookie).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a forgotten device needs a code again");
    }

    [Fact]
    public async Task A_device_is_trusted_for_its_own_account_only_and_turning_two_step_off_forgets_it()
    {
        var id = await Author();
        var other = await Author();
        var browser = PasswordOnly(id);
        await browser.PostAsync("/api/me/two-step/email/send", null);
        var verified = await browser.PostAsJsonAsync("/api/me/two-step/email/verify", new { code = LastCode(id), trustDevice = true });
        var cookie = verified.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(TwoStepService.DeviceCookie + "=")).Split(';')[0];

        (await PasswordOnly(other, cookie).GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "someone else's trusted device doesn't count");

        (await browser.PutAsJsonAsync("/api/me/two-step", new { enabled = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        (await db.TrustedDevices.AsNoTracking().CountAsync(d => d.UserId == id && d.RevokedAt == null)).Should().Be(0);
    }

    [Fact]
    public async Task Trusting_after_the_authenticator_needs_a_fresh_code()
    {
        var id = await Author();
        var now = DateTime.UtcNow;
        HttpClient With(DateTime totpAt)
        {
            var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            c.DefaultRequestHeaders.Add("X-Test-UserId", id);
            c.DefaultRequestHeaders.Add("X-Test-StytchSession", Session($"session-{Guid.NewGuid():N}", ("password", now), ("totp", totpAt)));
            return c;
        }
        (await With(now.AddHours(-1)).PostAsync("/api/me/two-step/trust", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var fresh = await With(now).PostAsync("/api/me/two-step/trust", null);
        fresh.StatusCode.Should().Be(HttpStatusCode.NoContent);
        fresh.Headers.GetValues("Set-Cookie").Should().Contain(h => h.StartsWith(TwoStepService.DeviceCookie + "="));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iPad")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0", "Firefox on Linux")]
    [InlineData("", "This browser")]
    public void Devices_are_named_from_the_browser(string ua, string label) => TwoStepService.DeviceLabel(ua).Should().Be(label);
}
