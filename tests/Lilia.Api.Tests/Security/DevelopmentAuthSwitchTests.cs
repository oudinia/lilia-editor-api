using FluentAssertions;
using Lilia.Api.Middleware;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lilia.Api.Tests.Security;

/// <summary>
/// The development sign-in (no token → a development user) must never answer
/// where real sign-in is configured. On 10 Oct 2026 the production API ran in
/// Development behind editor.liliaeditor.com with Stytch configured, and served
/// anyone's unsigned request as the owner's account.
/// </summary>
public class DevelopmentAuthSwitchTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

    [Fact]
    public void Off_when_Stytch_is_configured_even_in_Development()
    {
        DevelopmentAuthMiddleware.IsEnabled(Config(("Stytch:ProjectId", "project-test-x")), isDevelopment: true).Should().BeFalse();
    }

    [Fact]
    public void A_test_stack_turns_it_on_explicitly()
    {
        DevelopmentAuthMiddleware.IsEnabled(Config(("Stytch:ProjectId", "project-test-x"), ("Auth:DevelopmentAuth:Enabled", "true")), isDevelopment: true).Should().BeTrue();
    }

    [Fact]
    public void Never_outside_Development_and_never_when_disabled()
    {
        DevelopmentAuthMiddleware.IsEnabled(Config(), isDevelopment: false).Should().BeFalse();
        DevelopmentAuthMiddleware.IsEnabled(Config(("Auth:DevelopmentAuth:Enabled", "true")), isDevelopment: false).Should().BeFalse();
        DevelopmentAuthMiddleware.IsEnabled(Config(("Auth:DevelopmentAuth:Disabled", "true"), ("Auth:DevelopmentAuth:Enabled", "true")), isDevelopment: true).Should().BeFalse();
    }

    [Fact]
    public void A_bare_development_machine_without_any_sign_in_keeps_it()
    {
        DevelopmentAuthMiddleware.IsEnabled(Config(), isDevelopment: true).Should().BeTrue();
    }
}
