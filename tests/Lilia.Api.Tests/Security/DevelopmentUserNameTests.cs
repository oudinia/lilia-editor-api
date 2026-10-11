using System.Security.Claims;
using FluentAssertions;
using Lilia.Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Security;

/// <summary>
/// The test copy's "Sign in as…" (11 Oct) names its people: X-Development-User-Name
/// becomes the development user's name. Only where development sign-in is on.
/// </summary>
public class DevelopmentUserNameTests
{
    private sealed class Env : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Lilia.Api";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<ClaimsPrincipal?> Run(Action<HttpRequest> setup, params (string, string)[] config)
    {
        ClaimsPrincipal? seen = null;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config.ToDictionary(p => p.Item1, p => (string?)p.Item2)).Build();
        var mw = new DevelopmentAuthMiddleware(ctx => { seen = ctx.User; return Task.CompletedTask; }, cfg, new Env(), NullLogger<DevelopmentAuthMiddleware>.Instance);
        var context = new DefaultHttpContext();
        setup(context.Request);
        await mw.InvokeAsync(context);
        return seen;
    }

    [Fact]
    public async Task A_test_person_carries_the_name_they_were_given()
    {
        var user = await Run(r => { r.Headers["X-Development-User-Id"] = "ana"; r.Headers["X-Development-User-Name"] = "Ana Martin"; });
        user!.FindFirst("name")!.Value.Should().Be("Ana Martin");
        user.FindFirst("email")!.Value.Should().Be("ana@dev.liliaeditor.com");
    }

    [Fact]
    public async Task Without_a_name_the_old_label_stays()
    {
        var user = await Run(r => r.Headers["X-Development-User-Id"] = "ben");
        user!.FindFirst("name")!.Value.Should().Be("Dev User (ben)");
    }

    [Fact]
    public async Task Where_development_sign_in_is_off_the_headers_do_nothing()
    {
        var user = await Run(r => { r.Headers["X-Development-User-Id"] = "ana"; r.Headers["X-Development-User-Name"] = "Ana Martin"; },
            ("Auth:DevelopmentAuth:Disabled", "true"));
        user!.Identity?.IsAuthenticated.Should().NotBe(true);
        user.FindFirst("sub").Should().BeNull();
    }

    [Fact]
    public async Task A_test_person_can_carry_any_address()
    {
        var user = await Run(r => { r.Headers["X-Development-User-Id"] = "friend"; r.Headers["X-Development-User-Email"] = "friend@gmail.com"; });
        user!.FindFirst("email")!.Value.Should().Be("friend@gmail.com");
        var junk = await Run(r => { r.Headers["X-Development-User-Id"] = "x"; r.Headers["X-Development-User-Email"] = "not an address"; });
        junk!.FindFirst("email")!.Value.Should().Be("x@dev.liliaeditor.com");
    }
}
