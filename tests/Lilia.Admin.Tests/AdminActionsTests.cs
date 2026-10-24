using System.Net;
using FluentAssertions;
using Lilia.Admin.Data;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>Support actions: each does what it says, needs a reason, and is recorded with who and why.</summary>
[Collection("Admin")]
public class AdminActionsTests(AdminFixture fx)
{
    private const string Admin = AdminFixture.AdminEmail;

    private async Task<string> SeedUserAsync(string tag, Action<User>? setup = null)
    {
        var id = $"act-{tag}-{Guid.NewGuid():N}"[..28];
        await using var db = fx.CreateDbContext();
        var user = new User { Id = id, Email = $"{id}@lilia.test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        setup?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return id;
    }

    private AdminActions Actions(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AdminActions>();

    private async Task<List<AdminAction>> LogAsync(string userId)
    {
        await using var db = fx.CreateDbContext();
        return await db.AdminActions.AsNoTracking().Where(a => a.TargetUserId == userId).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task Granting_credits_adds_a_ledger_grant_and_records_who_and_why()
    {
        var id = await SeedUserAsync("grant");
        using var scope = fx.Factory.Services.CreateScope();
        var r = await Actions(scope).GrantCreditsAsync(Admin, id, 250, "Feedback #12: two failed answers");
        r.Ok.Should().BeTrue(r.Message);

        await using var db = fx.CreateDbContext();
        var ledger = await db.AiCreditLedger.AsNoTracking().SingleAsync(l => l.UserId == id);
        ledger.Delta.Should().Be(250);
        ledger.Reason.Should().Be("grant");
        ledger.Note.Should().Contain("Feedback #12");
        var log = (await LogAsync(id)).Single();
        log.Action.Should().Be("grant_credits");
        log.AdminEmail.Should().Be(Admin);
        log.Reason.Should().Be("Feedback #12: two failed answers");
        log.Details!.RootElement.GetProperty("amount").GetInt32().Should().Be(250);
    }

    [Fact]
    public async Task Without_a_reason_or_with_a_bad_amount_nothing_changes()
    {
        var id = await SeedUserAsync("noreason");
        using var scope = fx.Factory.Services.CreateScope();
        (await Actions(scope).GrantCreditsAsync(Admin, id, 100, "  ")).Ok.Should().BeFalse();
        (await Actions(scope).GrantCreditsAsync(Admin, id, 0, "a good reason")).Ok.Should().BeFalse();
        (await Actions(scope).GrantCreditsAsync(Admin, id, AdminActions.MaxCredits + 1, "a good reason")).Ok.Should().BeFalse();
        (await Actions(scope).SetSuspendedAsync(Admin, id, true, "why")).Ok.Should().BeFalse("a reason is 5 characters or more");
        (await Actions(scope).GrantCreditsAsync(Admin, "no-such-account", 10, "a good reason")).Ok.Should().BeFalse();

        await using var db = fx.CreateDbContext();
        (await db.AiCreditLedger.AnyAsync(l => l.UserId == id)).Should().BeFalse();
        (await db.Users.SingleAsync(u => u.Id == id)).Banned.Should().BeFalse();
        (await LogAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_the_plan_ends_the_old_one_and_free_just_ends_it()
    {
        var id = await SeedUserAsync("plan");
        using var scope = fx.Factory.Services.CreateScope();
        var plans = await Actions(scope).PlansAsync();
        plans.Should().NotBeEmpty("the migrations seed the plans");
        var paid = plans[^1].Slug;

        (await Actions(scope).SetPlanAsync(Admin, id, paid, "Beta tester, agreed by email")).Ok.Should().BeTrue();
        await using (var db = fx.CreateDbContext())
            (await db.UserPlans.CountAsync(p => p.UserId == id && p.Status == "active")).Should().Be(1);

        (await Actions(scope).SetPlanAsync(Admin, id, "free", "Beta period over")).Ok.Should().BeTrue();
        await using (var db = fx.CreateDbContext())
        {
            (await db.UserPlans.CountAsync(p => p.UserId == id && p.Status == "active")).Should().Be(0);
            (await db.UserPlans.CountAsync(p => p.UserId == id && p.Status == "cancelled")).Should().Be(1);
        }
        (await LogAsync(id)).Select(a => a.Action).Should().Equal("set_plan", "set_plan");
        (await Actions(scope).SetPlanAsync(Admin, id, "no-such-plan", "a good reason")).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Suspending_and_restoring_are_recorded_and_an_erased_account_is_left_alone()
    {
        var id = await SeedUserAsync("suspend");
        using var scope = fx.Factory.Services.CreateScope();
        (await Actions(scope).SetSuspendedAsync(Admin, id, true, "Abuse report #3")).Ok.Should().BeTrue();
        (await Actions(scope).SetSuspendedAsync(Admin, id, true, "Abuse report #3")).Ok.Should().BeFalse("already suspended");
        await using (var db = fx.CreateDbContext())
        {
            var u = await db.Users.SingleAsync(x => x.Id == id);
            u.Banned.Should().BeTrue();
            u.BanReason.Should().Be(AdminActions.SuspendedReason);
        }
        (await Actions(scope).SetSuspendedAsync(Admin, id, false, "Cleared after review")).Ok.Should().BeTrue();
        (await LogAsync(id)).Select(a => a.Action).Should().Equal("suspend", "unsuspend");

        var erased = await SeedUserAsync("erased", u => { u.Banned = true; u.BanReason = "erased"; });
        (await Actions(scope).SetSuspendedAsync(Admin, erased, false, "Trying to restore")).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_two_step_off_is_recorded_and_forgets_trusted_devices()
    {
        var id = await SeedUserAsync("2step", u => u.TwoStepEnabled = true);
        await using (var db = fx.CreateDbContext())
        {
            db.TrustedDevices.Add(new TrustedDevice
            {
                UserId = id, TokenHash = Guid.NewGuid().ToString("N"), Label = "Chrome on Windows",
                CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30),
            });
            await db.SaveChangesAsync();
        }
        using var scope = fx.Factory.Services.CreateScope();
        (await Actions(scope).TwoStepOffAsync(Admin, id, "no")).Ok.Should().BeFalse("a reason is 5 characters or more");
        var done = await Actions(scope).TwoStepOffAsync(Admin, id, "Lost phone, identity checked by email reply");
        done.Ok.Should().BeTrue();
        done.Message.Should().Contain("1 trusted device");
        (await Actions(scope).TwoStepOffAsync(Admin, id, "Lost phone, identity checked")).Ok.Should().BeFalse("already off");
        await using (var db = fx.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == id)).TwoStepEnabled.Should().BeFalse();
            (await db.TrustedDevices.AsNoTracking().CountAsync(d => d.UserId == id && d.RevokedAt == null)).Should().Be(0);
        }
        (await LogAsync(id)).Select(a => a.Action).Should().Equal("two_step_off");
    }

    [Fact]
    public async Task Resending_verification_goes_through_the_API_and_is_recorded()
    {
        var id = await SeedUserAsync("verify");
        var stub = new StubHandler();
        using var factory = fx.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Admin:ApiBase", "http://api.test");
            b.ConfigureServices(s => s.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName).ConfigurePrimaryHttpMessageHandler(() => stub));
        });
        using var scope = factory.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<AdminActions>().ResendVerificationAsync(Admin, id, "Never got the first email");
        r.Ok.Should().BeTrue(r.Message);
        stub.Requests.Should().ContainSingle().Which.Should().Be($"POST http://api.test/api/auth/resend-verification {{\"email\":\"{id}@lilia.test\"}}");
        (await LogAsync(id)).Single().Action.Should().Be("resend_verification");
    }

    [Fact]
    public async Task Feedback_triage_is_recorded_with_the_operator()
    {
        var id = Guid.NewGuid();
        var author = await SeedUserAsync("fb");
        await using (var db = fx.CreateDbContext())
        {
            db.Feedback.Add(new Feedback { Id = id, UserId = author, Type = "bug", Message = "x", Status = "new", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var scope = fx.Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AdminQueries>().SetFeedbackStatusAsync(id, "acknowledged", Admin)).Should().BeTrue();
        var log = (await LogAsync(author)).Single();
        log.Action.Should().Be("feedback_status");
        log.Details!.RootElement.GetProperty("to").GetString().Should().Be("acknowledged");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri} {await request.Content!.ReadAsStringAsync(ct)}");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
