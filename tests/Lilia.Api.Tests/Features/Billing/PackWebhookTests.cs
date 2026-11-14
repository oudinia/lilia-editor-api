using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using Xunit;

namespace Lilia.Api.Tests.Features.Billing;

/// <summary>
/// A paid pack Checkout credits the packs paid for, with the bonus, once (the owner, 14 Nov 2026: $5 for 300
/// operations, 375 each from 4 packs, 450 each from 10). Sent as Stripe sends it: a signed event to the webhook.
/// </summary>
public class PackWebhookTests : IntegrationTestBase, IDisposable
{
    private const string Secret = "whsec_test_packs";
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    public PackWebhookTests(TestDatabaseFixture fixture) : base(fixture) { }
    public void Dispose() { foreach (var f in _factories) f.Dispose(); }

    private HttpClient Stripe()
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Stripe:WebhookSecret", Secret);
            b.UseSetting("Stripe:OperationsPackUnitCents", "500");
        });
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static string NewId(string what) => $"pack_{what}_{Guid.NewGuid():N}"[..36];

    private static string Event(string type, string sessionId, string userId, long subtotal, string paymentStatus = "paid", string kind = Lilia.Api.Services.BillingService.PackKind) =>
        JsonSerializer.Serialize(new
        {
            id = "evt_" + Guid.NewGuid().ToString("N"),
            @object = "event",
            api_version = StripeConfiguration.ApiVersion,
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            type,
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    @object = "checkout.session",
                    mode = "payment",
                    payment_status = paymentStatus,
                    amount_subtotal = subtotal,
                    client_reference_id = userId,
                    metadata = new Dictionary<string, string> { ["user_id"] = userId, ["kind"] = kind },
                },
            },
        });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string payload, string secret = Secret)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}"))).ToLowerInvariant();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Add("Stripe-Signature", $"t={t},v1={sig}");
        return await client.SendAsync(req);
    }

    private async Task<int> BalanceAsync(string userId)
    {
        await using var db = CreateDbContext();
        return await new OperationPacks(db, NullLogger<OperationPacks>.Instance).BalanceAsync(userId);
    }

    [Fact]
    public async Task Four_packs_paid_credit_1500_once_whatever_Stripe_sends_twice()
    {
        var user = NewId("four");
        await SeedUserAsync(user);
        var client = Stripe();
        var session = "cs_test_" + Guid.NewGuid().ToString("N");

        (await SendAsync(client, Event("checkout.session.completed", session, user, 2000))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BalanceAsync(user)).Should().Be(1500);

        // The same session again, as a new event (Stripe's retries carry the same id; a resend may not).
        (await SendAsync(client, Event("checkout.session.completed", session, user, 2000))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BalanceAsync(user)).Should().Be(1500);
    }

    [Fact]
    public async Task Ten_packs_credit_4500()
    {
        var user = NewId("ten");
        await SeedUserAsync(user);
        await SendAsync(Stripe(), Event("checkout.session.completed", "cs_test_" + Guid.NewGuid().ToString("N"), user, 5000));
        (await BalanceAsync(user)).Should().Be(4500);
    }

    [Fact]
    public async Task An_unpaid_session_waits_for_its_payment()
    {
        var user = NewId("later");
        await SeedUserAsync(user);
        var client = Stripe();
        var session = "cs_test_" + Guid.NewGuid().ToString("N");
        await SendAsync(client, Event("checkout.session.completed", session, user, 500, paymentStatus: "unpaid"));
        (await BalanceAsync(user)).Should().Be(0);
        await SendAsync(client, Event("checkout.session.async_payment_succeeded", session, user, 500));
        (await BalanceAsync(user)).Should().Be(300);
    }

    [Fact]
    public async Task A_bad_signature_credits_nothing()
    {
        var user = NewId("forged");
        await SeedUserAsync(user);
        var res = await SendAsync(Stripe(), Event("checkout.session.completed", "cs_test_" + Guid.NewGuid().ToString("N"), user, 5000), secret: "whsec_wrong");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BalanceAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task A_membership_checkout_is_not_a_pack()
    {
        var user = NewId("member");
        await SeedUserAsync(user);
        await SendAsync(Stripe(), Event("checkout.session.completed", "cs_test_" + Guid.NewGuid().ToString("N"), user, 1099, kind: "membership"));
        (await BalanceAsync(user)).Should().Be(0);
    }
}
