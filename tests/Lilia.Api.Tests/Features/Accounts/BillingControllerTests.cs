using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.Accounts;

/// <summary>
/// Billing had no test (2 Nov 2026, the coverage round). The plan list is public; a person's plan,
/// checkout and the portal need a sign-in; the Stripe webhook is anonymous and must refuse a call
/// without Stripe's signature. Stripe isn't configured in tests, so checkout says billing is
/// unavailable rather than failing.
/// </summary>
public class BillingControllerTests : IntegrationTestBase
{
    public BillingControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_plan_list_is_public_and_the_rest_needs_a_sign_in()
    {
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync("/api/billing/plans")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync("/api/plan")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/billing/checkout", new { planSlug = "pro" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsync("/api/billing/portal", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The connector-first launch (10 Nov 2026): Pro Plus is hidden while Ask Lilia is "coming soon"; Free and Pro are listed.
    [Fact]
    public async Task The_plan_list_offers_free_and_pro_not_pro_plus()
    {
        using var anon = CreateAnonymousClient();
        var body = await anon.GetStringAsync("/api/billing/plans");
        body.Should().Contain("\"slug\":\"free\"").And.Contain("\"slug\":\"pro\"").And.NotContain("\"slug\":\"pro_plus\"");
    }

    [Fact]
    public async Task A_signed_in_person_sees_their_plan_and_checkout_needs_a_plan()
    {
        await SeedUserAsync("billing_reader");
        using var c = CreateClientAs("billing_reader");
        (await c.GetAsync("/api/plan")).StatusCode.Should().Be(HttpStatusCode.OK);
        var noPlan = await c.PostAsJsonAsync("/api/billing/checkout", new { planSlug = "" });
        noPlan.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noPlan.Content.ReadAsStringAsync()).Should().Contain("plan_slug_required");
        // No Stripe key in tests: a clear "unavailable", not a server error.
        (await c.PostAsJsonAsync("/api/billing/checkout", new { planSlug = "pro" })).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_webhook_refuses_a_call_without_stripes_signature()
    {
        using var anon = CreateAnonymousClient();
        var r = await anon.PostAsync("/api/billing/webhook",
            new StringContent("{\"type\":\"checkout.session.completed\"}", System.Text.Encoding.UTF8, "application/json"));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("webhook_rejected");
    }
}
