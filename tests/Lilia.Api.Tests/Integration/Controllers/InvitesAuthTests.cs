using System.Net;
using System.Reflection;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Accept and Decline are authenticated by the framework; Resolve stays public.
/// They had no attribute and returned 401 only because the handler checked the user
/// id itself (cloud session auth probe, 2 Oct 2026): one refactor away from open.
/// </summary>
[Collection("Integration")]
public class InvitesAuthTests : IntegrationTestBase
{
    public InvitesAuthTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public void The_controller_requires_login_and_only_Resolve_is_public()
    {
        typeof(InvitesController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        foreach (var m in typeof(InvitesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var anon = m.GetCustomAttribute<AllowAnonymousAttribute>() != null;
            anon.Should().Be(m.Name == nameof(InvitesController.Resolve), $"{m.Name} is {(anon ? "public" : "authenticated")}");
        }
    }

    [Fact]
    public async Task Accept_and_Decline_answer_an_anonymous_caller_with_401()
    {
        using var anon = CreateAnonymousClient();
        var token = Guid.NewGuid();
        (await anon.PostAsync($"/api/invites/{token}/accept", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.DeleteAsync($"/api/invites/{token}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resolve_stays_reachable_without_a_login()
    {
        using var anon = CreateAnonymousClient();
        var res = await anon.GetAsync($"/api/invites/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);   // an unknown token, not a 401
    }

    [Fact]
    public async Task A_signed_in_user_still_reaches_accept()
    {
        var res = await Client.PostAsync($"/api/invites/{Guid.NewGuid()}/accept", null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);   // unknown invite, not blocked
    }
}
