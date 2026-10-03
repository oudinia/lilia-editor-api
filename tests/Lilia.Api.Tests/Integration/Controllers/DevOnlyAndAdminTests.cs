using System.Net;
using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Security;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// T3 of the cloud session's audit (2 Oct 2026): the E2E and DevTools controllers do not
/// exist in a Production API, and the Typst coverage admin routes and the compile-queue
/// metrics need an admin, not just a login.
/// </summary>
[Collection("Integration")]
public class DevOnlyAndAdminTests : IntegrationTestBase
{
    public DevOnlyAndAdminTests(TestDatabaseFixture fixture) : base(fixture) { }

    // ── which controllers an environment registers ───────────────────────

    private static HashSet<Type> ControllersIn(string environment, Dictionary<string, string?>? config = null)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config ?? new()).Build();
        var manager = new ApplicationPartManager();
        manager.ApplicationParts.Add(new AssemblyPart(typeof(E2EController).Assembly));
        manager.FeatureProviders.Add(new DevOnlyControllerFeatureProvider(env.Object, cfg));
        var feature = new ControllerFeature();
        manager.PopulateFeature(feature);
        return feature.Controllers.Select(t => t.AsType()).ToHashSet();
    }

    [Theory]
    [InlineData("Production")]
    public void Production_has_no_E2E_or_DevTools_controller(string env)
    {
        var all = ControllersIn(env);
        all.Should().NotContain(typeof(E2EController));
        all.Should().NotContain(typeof(DevToolsController));
        all.Should().Contain(typeof(DocumentsController), "everything else is still there");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void Other_environments_keep_them(string env)
    {
        var all = ControllersIn(env);
        all.Should().Contain(typeof(E2EController));
        all.Should().Contain(typeof(DevToolsController));
    }

    [Fact]
    public void A_staging_API_running_as_Production_can_opt_back_in()
    {
        var all = ControllersIn("Production", new() { ["Features:ExposeE2E"] = "true" });
        all.Should().Contain(typeof(E2EController));
    }

    // ── who is an admin ──────────────────────────────────────────────────

    private static ClaimsPrincipal User(string id, string? email = null, string? role = null, bool authenticated = true)
    {
        var claims = new List<Claim> { new("sub", id) };
        if (email != null) claims.Add(new Claim("email", email));
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity());
    }

    [Fact]
    public void Nobody_is_an_admin_by_default()
    {
        AdminPolicy.IsAdmin(User("u1", "a@b.c"), null).Should().BeFalse();
        AdminPolicy.IsAdmin(User("u1", "a@b.c"), "").Should().BeFalse();
    }

    [Fact]
    public void The_admin_role_counts()
    {
        AdminPolicy.IsAdmin(User("u1", role: "admin"), null).Should().BeTrue();
        AdminPolicy.IsAdmin(User("u1", role: "editor"), null).Should().BeFalse();
    }

    [Fact]
    public void A_configured_user_id_or_email_counts_case_insensitively()
    {
        AdminPolicy.IsAdmin(User("user_123"), "user_123, other").Should().BeTrue();
        AdminPolicy.IsAdmin(User("u9", "Boss@Example.com"), "boss@example.com").Should().BeTrue();
        AdminPolicy.IsAdmin(User("u9", "x@example.com"), "boss@example.com").Should().BeFalse();
    }

    [Fact]
    public void An_unauthenticated_caller_is_never_an_admin_even_when_named()
    {
        AdminPolicy.IsAdmin(User("user_123", authenticated: false), "user_123").Should().BeFalse();
    }

    // ── the routes ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/admin/typst-coverage")]
    [InlineData("/api/latex/metrics")]
    public async Task A_signed_in_non_admin_gets_403(string url)
    {
        (await Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/admin/typst-coverage")]
    [InlineData("/api/latex/metrics")]
    public async Task An_anonymous_caller_gets_401(string url)
    {
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
